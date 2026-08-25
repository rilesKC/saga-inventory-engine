namespace OrderSaga.Shared.Tests;

public class InboundOutboundEventBusTests
{
    private sealed record TestEvent(string Value);

    [Fact]
    public void InboundEventBus_Subscribe_ReceivesEventsPublishedOnTheWrappedBus()
    {
        var bus = new EventBus();
        var inbound = new InboundEventBus(bus);
        TestEvent? received = null;
        inbound.Subscribe<TestEvent>(e => received = e);

        bus.Publish(new TestEvent("hello"));

        Assert.NotNull(received);
        Assert.Equal("hello", received.Value);
    }

    [Fact]
    public void OutboundEventBus_Publish_IsObservableOnTheWrappedBus()
    {
        var bus = new EventBus();
        var outbound = new OutboundEventBus(bus);
        TestEvent? received = null;
        bus.Subscribe<TestEvent>(e => received = e);

        outbound.Publish(new TestEvent("hello"));

        Assert.NotNull(received);
        Assert.Equal("hello", received.Value);
    }
}
