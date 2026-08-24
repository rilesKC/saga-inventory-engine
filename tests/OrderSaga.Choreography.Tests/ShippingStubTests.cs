using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Tests;

public class ShippingStubTests
{
    [Fact]
    public void OnReservationConfirmed_PublishesShipmentScheduled()
    {
        var bus = new EventBus();
        _ = new ShippingStub(bus);
        ShipmentScheduled? published = null;
        bus.Subscribe<ShipmentScheduled>(e => published = e);

        bus.Publish(new ReservationConfirmed("SKU-1", "ORDER-1", 4));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
    }
}
