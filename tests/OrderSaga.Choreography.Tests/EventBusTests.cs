namespace OrderSaga.Choreography.Tests;

public class EventBusTests
{
    private sealed record TestEvent(string Value);

    [Fact]
    public void Publish_InvokesSubscribedHandlerWithTheEvent()
    {
        var bus = new EventBus();
        TestEvent? received = null;
        bus.Subscribe<TestEvent>(e => received = e);

        bus.Publish(new TestEvent("hello"));

        Assert.NotNull(received);
        Assert.Equal("hello", received.Value);
    }

    [Fact]
    public void Publish_WithNoSubscribers_DoesNothing()
    {
        var bus = new EventBus();

        var exception = Record.Exception(() => bus.Publish(new TestEvent("hello")));

        Assert.Null(exception);
    }
}
