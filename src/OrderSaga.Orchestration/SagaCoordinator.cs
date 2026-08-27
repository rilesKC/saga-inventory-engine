using OrderSaga.Shared;

namespace OrderSaga.Orchestration;

public sealed class SagaCoordinator
{
    private readonly InboundEventBus _inbound;
    private readonly OutboundEventBus _outbound;
    private readonly ISagaStateStore _store;

    // Cache for GetStep only -- an observability convenience, not the source of truth for command
    // processing. Every handler reloads fresh from _store via ApplyWithRetry before mutating, so a
    // stale entry here (from another Host instance's concurrent write) never causes incorrect
    // behavior, only a possibly-stale read for whoever calls GetStep.
    private readonly Dictionary<string, SagaState> _sagas = [];

    /// <param name="inbound">Subscribed to for the trigger event and every reply.</param>
    /// <param name="outbound">Published to for issued commands. In-process orchestration (this
    /// project's own tests) wraps the same underlying EventBus for both. The Host layer
    /// (saga-inventory-engine's AWS deployment) wraps two separate EventBus instances -- sharing
    /// one here would let this coordinator's own issued commands loop back through its own reply
    /// subscriptions in-process, the same class of bug choreography's InventoryParticipant/
    /// PaymentStub/ShippingStub were fixed for.</param>
    /// <param name="store">Durable, shared source of truth for SagaState -- every command reloads
    /// the latest state from here immediately before mutating it (see ApplyWithRetry), rather than
    /// mutating one cached in-memory object. That reload is what makes desired_count >= 2 safe: two
    /// concurrently-running Host instances no longer each hold their own drifting copy of a saga's
    /// state.</param>
    public SagaCoordinator(InboundEventBus inbound, OutboundEventBus outbound, ISagaStateStore store)
    {
        _inbound = inbound;
        _outbound = outbound;
        _store = store;
        foreach (var saga in _store.LoadAllAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            _sagas[saga.OrderId] = saga;
        }

        _inbound.Subscribe<OrderPlaced>(OnOrderPlaced);
        _inbound.Subscribe<StockReservedReply>(OnStockReservedReply);
        _inbound.Subscribe<StockReservationFailedReply>(OnStockReservationFailedReply);
        _inbound.Subscribe<PaymentChargedReply>(OnPaymentChargedReply);
        _inbound.Subscribe<PaymentDeclinedReply>(OnPaymentDeclinedReply);
        _inbound.Subscribe<ReservationConfirmedReply>(OnReservationConfirmedReply);
        _inbound.Subscribe<ReservationReleasedReply>(OnReservationReleasedReply);
        _inbound.Subscribe<ShipmentScheduledReply>(OnShipmentScheduledReply);
    }

    public SagaStep GetStep(string orderId) => _sagas[orderId].Step;

    private void OnOrderPlaced(OrderPlaced orderPlaced)
    {
        // No client-supplied idempotency key exists at the HTTP intake layer, so a plain retried
        // POST /orders (a timeout, a double-click, a back-button resubmit) mints a brand-new
        // envelope MessageId and sails straight past the transport-level idempotency store -- this
        // handler is the only thing that can still catch a duplicate OrderPlaced for an OrderId
        // that already has a saga. `alreadyExists` gates the *publish* (a best-effort check, since
        // a genuinely concurrent duplicate could still race past it -- the downstream
        // InventoryResponder's own OrderId dedup absorbs that narrow case harmlessly). The
        // transition passed to ApplyWithRetry is what makes the *stored state* safe unconditionally:
        // `current ?? new SagaState(...)` never overwrites an existing saga even under a genuine
        // race, so an already-in-flight or already-completed order can never be reset back to
        // ReservingStock.
        var alreadyExists = _store.TryLoadAsync(orderPlaced.OrderId, CancellationToken.None).GetAwaiter().GetResult() is not null;

        ApplyWithRetry(orderPlaced.OrderId, current => current ?? new SagaState(
            orderPlaced.OrderId, orderPlaced.Sku, orderPlaced.Quantity, orderPlaced.Amount, SagaStep.ReservingStock));

        if (!alreadyExists)
        {
            _outbound.Publish(new ReserveStockCommand(orderPlaced.OrderId, orderPlaced.Sku, orderPlaced.Quantity, orderPlaced.Amount));
        }
    }

    private void OnStockReservedReply(StockReservedReply reply)
    {
        var saga = ApplyWithRetry(reply.OrderId, current => IfAtStep(current, reply.OrderId, SagaStep.ReservingStock, SagaStep.AwaitingPayment));
        if (saga is not null)
        {
            _outbound.Publish(new ChargePaymentCommand(reply.OrderId, reply.Sku, saga.Amount));
        }
    }

    private void OnStockReservationFailedReply(StockReservationFailedReply reply) =>
        ApplyWithRetry(reply.OrderId, current => IfAtStep(current, reply.OrderId, SagaStep.ReservingStock, SagaStep.Failed));

    private void OnPaymentChargedReply(PaymentChargedReply reply)
    {
        var saga = ApplyWithRetry(reply.OrderId, current => IfAtStep(current, reply.OrderId, SagaStep.AwaitingPayment, SagaStep.Confirming));
        if (saga is not null)
        {
            _outbound.Publish(new ConfirmReservationCommand(reply.OrderId, reply.Sku));
        }
    }

    private void OnPaymentDeclinedReply(PaymentDeclinedReply reply)
    {
        var saga = ApplyWithRetry(reply.OrderId, current => IfAtStep(current, reply.OrderId, SagaStep.AwaitingPayment, SagaStep.Compensating));
        if (saga is not null)
        {
            _outbound.Publish(new ReleaseReservationCommand(reply.OrderId, reply.Sku));
        }
    }

    private void OnReservationConfirmedReply(ReservationConfirmedReply reply)
    {
        var saga = ApplyWithRetry(reply.OrderId, current => IfAtStep(current, reply.OrderId, SagaStep.Confirming, SagaStep.SchedulingShipment));
        if (saga is not null)
        {
            _outbound.Publish(new ScheduleShipmentCommand(reply.OrderId, reply.Sku));
        }
    }

    private void OnReservationReleasedReply(ReservationReleasedReply reply) =>
        ApplyWithRetry(reply.OrderId, current => IfAtStep(current, reply.OrderId, SagaStep.Compensating, SagaStep.Compensated));

    private void OnShipmentScheduledReply(ShipmentScheduledReply reply) =>
        ApplyWithRetry(reply.OrderId, current => IfAtStep(current, reply.OrderId, SagaStep.SchedulingShipment, SagaStep.Completed));

    /// <summary>
    /// Every handler above except OnOrderPlaced requires a saga to already exist for this OrderId
    /// -- a reply arriving for an order the coordinator has no persisted SagaState for (e.g. its
    /// OrderPlaced write never landed, or the state was lost) must fail loudly and diagnosably
    /// rather than with an opaque NullReferenceException from blindly dereferencing null.
    /// </summary>
    private static SagaState RequireExisting(SagaState? current, string orderId) =>
        current ?? throw new SagaNotFoundException(orderId);

    /// <summary>
    /// A reply handler's shared guard: only apply the transition if the saga is actually at the
    /// step this reply expects to advance it from. Without this, a stale or duplicate reply (e.g.
    /// a redelivered SQS message arriving after the saga has already advanced further) would
    /// unconditionally overwrite Step -- regressing an already-advanced saga backward and, for
    /// handlers that publish, re-issuing a command that was already issued once. Returns null (a
    /// no-op, same idiom ApplyWithRetry's callers below rely on) when the saga isn't at
    /// expectedStep; the caller must not treat that as "safe to also skip" for OnOrderPlaced, which
    /// has its own null-never transition and is unaffected by this helper.
    /// </summary>
    private static SagaState? IfAtStep(SagaState? current, string orderId, SagaStep expectedStep, SagaStep nextStep)
    {
        var existing = RequireExisting(current, orderId);
        return existing.Step == expectedStep ? existing with { Step = nextStep } : null;
    }

    /// <summary>
    /// Reloads SagaState from the durable store, applies the transition, and saves the result
    /// guarded by optimistic concurrency -- retrying from a fresh reload if a concurrent writer
    /// (the other Host instance) already saved a newer version first. transition receives the
    /// current state (null only for a brand-new order, i.e. OnOrderPlaced) and returns the new
    /// state to save, or null for a no-op (the saga isn't at the step this transition expects --
    /// see IfAtStep) -- nothing is saved and this returns null, mirroring the
    /// UncommittedEvents.Count == 0 early-return idiom InventoryParticipant/InventoryResponder's
    /// own ApplyWithRetry already use. Bounded via RetryBackoff -- under sustained contention
    /// (every attempt conflicts), the last ConcurrencyConflictException propagates to the caller
    /// instead of retrying forever.
    /// </summary>
    private SagaState? ApplyWithRetry(string orderId, Func<SagaState?, SagaState?> transition)
    {
        for (var attempt = 1; ; attempt++)
        {
            var current = _store.TryLoadAsync(orderId, CancellationToken.None).GetAwaiter().GetResult();
            var candidate = transition(current);

            if (candidate is null)
            {
                return null;
            }

            var next = candidate with { Version = (current?.Version ?? 0) + 1 };

            try
            {
                // EventBus.Subscribe<T> only takes a synchronous Action<T>, so this can't be
                // awaited up through that chain -- same constraint as InventoryParticipant/
                // InventoryResponder's own event-store append calls.
                _store.SaveAsync(next, current?.Version ?? 0, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (ConcurrencyConflictException) when (attempt < RetryBackoff.MaxAttempts)
            {
                RetryBackoff.WaitBeforeRetry(attempt);
                continue;
            }

            _sagas[next.OrderId] = next;
            return next;
        }
    }
}
