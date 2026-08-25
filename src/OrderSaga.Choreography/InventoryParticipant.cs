using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Choreography;

public sealed class InventoryParticipant
{
    private readonly InboundEventBus _inbound;
    private readonly OutboundEventBus _outbound;
    private readonly Dictionary<string, InventoryItem> _items;

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
    public InventoryParticipant(InboundEventBus inbound, OutboundEventBus outbound, Dictionary<string, InventoryItem> items)
    {
        _inbound = inbound;
        _outbound = outbound;
        _items = items;
        _inbound.Subscribe<OrderPlaced>(OnOrderPlaced);
        _inbound.Subscribe<PaymentCharged>(OnPaymentCharged);
        _inbound.Subscribe<PaymentDeclined>(OnPaymentDeclined);
    }

    private void OnOrderPlaced(OrderPlaced orderPlaced)
    {
        var item = _items[orderPlaced.Sku];
        var eventCountBefore = item.UncommittedEvents.Count;

        try
        {
            item.Handle(new ReserveStock(orderPlaced.Sku, orderPlaced.OrderId, orderPlaced.Quantity));
        }
        catch (InsufficientStockException)
        {
            _outbound.Publish(new StockReservationFailed(orderPlaced.OrderId, orderPlaced.Sku));
            return;
        }

        PublishNewEvents(item, eventCountBefore);
    }

    private void OnPaymentCharged(PaymentCharged paymentCharged)
    {
        var item = _items[paymentCharged.Sku];
        var eventCountBefore = item.UncommittedEvents.Count;

        item.Handle(new ConfirmReservation(paymentCharged.Sku, paymentCharged.OrderId));

        PublishNewEvents(item, eventCountBefore);
    }

    private void OnPaymentDeclined(PaymentDeclined paymentDeclined)
    {
        var item = _items[paymentDeclined.Sku];
        var eventCountBefore = item.UncommittedEvents.Count;

        item.Handle(new ReleaseReservation(paymentDeclined.Sku, paymentDeclined.OrderId));

        PublishNewEvents(item, eventCountBefore);
    }

    private void PublishNewEvents(InventoryItem item, int eventCountBefore)
    {
        for (var i = eventCountBefore; i < item.UncommittedEvents.Count; i++)
        {
            _outbound.Publish(item.UncommittedEvents[i]);
        }
    }
}
