using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Tests;

public class ShippingResponderTests
{
    [Fact]
    public void OnScheduleShipmentCommand_PublishesShipmentScheduledReply()
    {
        var bus = new EventBus();
        _ = new ShippingResponder(new InboundEventBus(bus), new OutboundEventBus(bus));
        ShipmentScheduledReply? published = null;
        bus.Subscribe<ShipmentScheduledReply>(e => published = e);

        bus.Publish(new ScheduleShipmentCommand("ORDER-1", "SKU-1"));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
    }
}
