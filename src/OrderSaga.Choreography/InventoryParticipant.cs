using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Choreography;

public sealed class InventoryParticipant
{
    private readonly EventBus _bus;
    private readonly Dictionary<string, InventoryItem> _items;

    public InventoryParticipant(EventBus bus, Dictionary<string, InventoryItem> items)
    {
        _bus = bus;
        _items = items;
        _bus.Subscribe<OrderPlaced>(OnOrderPlaced);
        _bus.Subscribe<PaymentCharged>(OnPaymentCharged);
        _bus.Subscribe<PaymentDeclined>(OnPaymentDeclined);
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
            _bus.Publish(new StockReservationFailed(orderPlaced.OrderId, orderPlaced.Sku));
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
            _bus.Publish(item.UncommittedEvents[i]);
        }
    }
}
