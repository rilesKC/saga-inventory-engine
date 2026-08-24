using Inventory.Domain;

namespace OrderSaga.Choreography.Tests;

public class OrderSagaChoreographyIntegrationTests
{
    private static (EventBus Bus, InventoryItem Item) WireSaga(string sku, int initialQuantity, decimal threshold)
    {
        var bus = new EventBus();
        var item = InventoryItem.Seed(sku, initialQuantity);
        _ = new InventoryParticipant(bus, new Dictionary<string, InventoryItem> { [sku] = item });
        _ = new PaymentStub(bus, threshold);
        _ = new ShippingStub(bus);
        return (bus, item);
    }

    [Fact]
    public void OrderPlaced_HappyPath_EndsWithShipmentScheduledAndReservationConfirmed()
    {
        var (bus, item) = WireSaga("SKU-1", 10, 500m);
        ShipmentScheduled? shipmentScheduled = null;
        bus.Subscribe<ShipmentScheduled>(e => shipmentScheduled = e);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(shipmentScheduled);
        Assert.Equal("ORDER-1", shipmentScheduled.OrderId);
        Assert.Equal(6, item.AvailableQuantity);
        Assert.Equal(4, item.DeductedQuantity);
        Assert.Equal(0, item.ReservedQuantity);
    }
}
