using Inventory.Domain;

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
    }

    private void OnOrderPlaced(OrderPlaced orderPlaced)
    {
        var item = _items[orderPlaced.Sku];
        var eventCountBefore = item.UncommittedEvents.Count;

        item.Handle(new ReserveStock(orderPlaced.Sku, orderPlaced.OrderId, orderPlaced.Quantity));

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
