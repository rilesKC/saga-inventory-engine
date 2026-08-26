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
        ApplyWithRetry(orderPlaced.OrderId, _ => new SagaState(
            orderPlaced.OrderId, orderPlaced.Sku, orderPlaced.Quantity, orderPlaced.Amount, SagaStep.ReservingStock));

        _outbound.Publish(new ReserveStockCommand(orderPlaced.OrderId, orderPlaced.Sku, orderPlaced.Quantity, orderPlaced.Amount));
    }

    private void OnStockReservedReply(StockReservedReply reply)
    {
        var saga = ApplyWithRetry(reply.OrderId, current => RequireExisting(current, reply.OrderId) with { Step = SagaStep.AwaitingPayment });
        _outbound.Publish(new ChargePaymentCommand(reply.OrderId, reply.Sku, saga.Amount));
    }

    private void OnStockReservationFailedReply(StockReservationFailedReply reply) =>
        ApplyWithRetry(reply.OrderId, current => RequireExisting(current, reply.OrderId) with { Step = SagaStep.Failed });

    private void OnPaymentChargedReply(PaymentChargedReply reply)
    {
        ApplyWithRetry(reply.OrderId, current => RequireExisting(current, reply.OrderId) with { Step = SagaStep.Confirming });
        _outbound.Publish(new ConfirmReservationCommand(reply.OrderId, reply.Sku));
    }

    private void OnPaymentDeclinedReply(PaymentDeclinedReply reply)
    {
        ApplyWithRetry(reply.OrderId, current => RequireExisting(current, reply.OrderId) with { Step = SagaStep.Compensating });
        _outbound.Publish(new ReleaseReservationCommand(reply.OrderId, reply.Sku));
    }

    private void OnReservationConfirmedReply(ReservationConfirmedReply reply)
    {
        ApplyWithRetry(reply.OrderId, current => RequireExisting(current, reply.OrderId) with { Step = SagaStep.SchedulingShipment });
        _outbound.Publish(new ScheduleShipmentCommand(reply.OrderId, reply.Sku));
    }

    private void OnReservationReleasedReply(ReservationReleasedReply reply) =>
        ApplyWithRetry(reply.OrderId, current => RequireExisting(current, reply.OrderId) with { Step = SagaStep.Compensated });

    private void OnShipmentScheduledReply(ShipmentScheduledReply reply) =>
        ApplyWithRetry(reply.OrderId, current => RequireExisting(current, reply.OrderId) with { Step = SagaStep.Completed });

    /// <summary>
    /// Every handler above except OnOrderPlaced requires a saga to already exist for this OrderId
    /// -- a reply arriving for an order the coordinator has no persisted SagaState for (e.g. its
    /// OrderPlaced write never landed, or the state was lost) must fail loudly and diagnosably
    /// rather than with an opaque NullReferenceException from blindly dereferencing null.
    /// </summary>
    private static SagaState RequireExisting(SagaState? current, string orderId) =>
        current ?? throw new SagaNotFoundException(orderId);

    /// <summary>
    /// Reloads SagaState from the durable store, applies the transition, and saves the result
    /// guarded by optimistic concurrency -- retrying from a fresh reload if a concurrent writer
    /// (the other Host instance) already saved a newer version first. transition receives the
    /// current state (null only for a brand-new order, i.e. OnOrderPlaced) and returns the new
    /// state to save. Bounded via RetryBackoff -- under sustained contention (every attempt
    /// conflicts), the last ConcurrencyConflictException propagates to the caller instead of
    /// retrying forever.
    /// </summary>
    private SagaState ApplyWithRetry(string orderId, Func<SagaState?, SagaState> transition)
    {
        for (var attempt = 1; ; attempt++)
        {
            var current = _store.TryLoadAsync(orderId, CancellationToken.None).GetAwaiter().GetResult();
            var next = transition(current) with { Version = (current?.Version ?? 0) + 1 };

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
