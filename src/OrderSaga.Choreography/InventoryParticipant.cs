using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Choreography;

public sealed class InventoryParticipant
{
    private readonly InboundEventBus _inbound;
    private readonly OutboundEventBus _outbound;
    private readonly IInventoryEventStore _eventStore;

    /// <param name="inbound">Subscribed to for trigger events.</param>
    /// <param name="outbound">Published to for produced events. In-process choreography (this
    /// project's own tests) wraps the same underlying EventBus for both -- direct
    /// participant-to-participant reaction is exactly what's being tested there. The Host layer
    /// (saga-inventory-engine's AWS deployment) wraps two separate EventBus instances so a
    /// participant's output only reaches the EventBridge-forwarding layer, not sibling participants
    /// directly -- every cross-participant reaction there is meant to happen via the real SQS
    /// round-trip, not an in-process shortcut. InboundEventBus/OutboundEventBus being distinct
    /// types (rather than both just EventBus) means the compiler catches the two parameters being
    /// swapped -- an unbounded republish loop was caused by exactly that shape of bug once already.</param>
    /// <param name="eventStore">Durable, shared source of truth for this SKU's InventoryItem --
    /// every command reloads the latest state from here immediately before mutating it (see
    /// ApplyWithRetry), rather than mutating one cached in-memory object. That reload is what makes
    /// desired_count >= 2 safe: two concurrently-running Host instances no longer each hold their
    /// own drifting copy of InventoryItem.</param>
    public InventoryParticipant(InboundEventBus inbound, OutboundEventBus outbound, IInventoryEventStore eventStore)
    {
        _inbound = inbound;
        _outbound = outbound;
        _eventStore = eventStore;
        _inbound.Subscribe<OrderPlaced>(OnOrderPlaced);
        _inbound.Subscribe<PaymentCharged>(OnPaymentCharged);
        _inbound.Subscribe<PaymentDeclined>(OnPaymentDeclined);
    }

    private void OnOrderPlaced(OrderPlaced orderPlaced)
    {
        try
        {
            ApplyWithRetry(orderPlaced.Sku, item =>
                item.Handle(new ReserveStock(orderPlaced.Sku, orderPlaced.OrderId, orderPlaced.Quantity, orderPlaced.Amount)));
        }
        catch (InsufficientStockException)
        {
            _outbound.Publish(new StockReservationFailed(orderPlaced.OrderId, orderPlaced.Sku));
        }
    }

    private void OnPaymentCharged(PaymentCharged paymentCharged)
    {
        try
        {
            ApplyWithRetry(paymentCharged.Sku, item =>
                item.Handle(new ConfirmReservation(paymentCharged.Sku, paymentCharged.OrderId)));
        }
        catch (InvalidReservationStateException)
        {
            // Redelivered/duplicate PaymentCharged for an order already Confirmed or Released --
            // no further action needed either way; choreography has no reply to send (unlike
            // orchestration's InventoryResponder, which must always answer its caller).
        }
    }

    private void OnPaymentDeclined(PaymentDeclined paymentDeclined)
    {
        try
        {
            ApplyWithRetry(paymentDeclined.Sku, item =>
                item.Handle(new ReleaseReservation(paymentDeclined.Sku, paymentDeclined.OrderId)));
        }
        catch (InvalidReservationStateException)
        {
            // Redelivered/duplicate PaymentDeclined for an order already Confirmed or Released.
        }
    }

    /// <summary>
    /// Reloads InventoryItem from the durable event log, applies the mutation, and appends the
    /// resulting new event(s) guarded by optimistic concurrency -- retrying from a fresh reload if
    /// a concurrent writer (the other Host instance) already appended first. mutate is expected to
    /// throw for a domain-level rejection (e.g. InsufficientStockException); that propagates to the
    /// caller unchanged, without appending anything. Bounded via RetryBackoff -- under sustained
    /// contention (every attempt conflicts), the last ConcurrencyConflictException propagates to
    /// the caller instead of retrying forever.
    ///
    /// Deliberately does NOT publish the new event(s) to _outbound here -- that used to happen
    /// inline, right after the append succeeded, but a crash (or a transient _outbound.Publish
    /// failure) in the gap between "durably appended" and "published" lost the event permanently:
    /// a redelivery's mutate(item) call sees the reservation already in its target state and
    /// produces zero new events, so this method returned early without ever retrying the publish.
    /// AppendRangeAsync now marks each new event pending-publish atomically as part of the same
    /// write; OutboxDrainerBackgroundService (in the Host project) is what actually publishes them,
    /// polling independently of this synchronous command-handling path. That trades a small amount
    /// of latency (other participants see the event on the next drain cycle, not this same tick)
    /// for the event never being silently lost.
    /// </summary>
    private void ApplyWithRetry(string sku, Action<InventoryItem> mutate)
    {
        for (var attempt = 1; ; attempt++)
        {
            var history = _eventStore.LoadEventsAsync(sku, CancellationToken.None).GetAwaiter().GetResult();
            var item = InventoryItem.LoadFromHistory(history);

            mutate(item);

            if (item.UncommittedEvents.Count == 0)
            {
                return;
            }

            try
            {
                _eventStore.AppendRangeAsync(sku, history.Count, item.UncommittedEvents, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (ConcurrencyConflictException) when (attempt < RetryBackoff.MaxAttempts)
            {
                RetryBackoff.WaitBeforeRetry(attempt);
                continue;
            }

            return;
        }
    }
}
