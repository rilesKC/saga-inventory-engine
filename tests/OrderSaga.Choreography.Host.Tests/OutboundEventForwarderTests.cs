using OrderSaga.Shared;

namespace OrderSaga.Choreography.Host.Tests;

public class OutboundEventForwarderTests
{
    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public readonly List<object> Published = [];

        public void Publish(object @event) => Published.Add(@event);
    }

    [Fact]
    public void Publish_OrderPlaced_ForwardsToEventPublisher()
    {
        var bus = new EventBus();
        var publisher = new RecordingEventPublisher();
        _ = new OutboundEventForwarder(bus, publisher);
        var orderPlaced = new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m);

        bus.Publish(orderPlaced);

        var forwarded = Assert.Single(publisher.Published);
        Assert.Equal(orderPlaced, forwarded);
    }
}
