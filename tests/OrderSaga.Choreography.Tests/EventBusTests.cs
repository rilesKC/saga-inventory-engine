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

    private sealed record FirstEvent;
    private sealed record SecondEvent;

    [Fact]
    public void Publish_NestedPublishDuringDispatch_ProcessesBreadthFirst()
    {
        var bus = new EventBus();
        var order = new List<string>();
        bus.Subscribe<FirstEvent>(_ =>
        {
            order.Add("first-handler-A");
            bus.Publish(new SecondEvent());
        });
        bus.Subscribe<FirstEvent>(_ => order.Add("first-handler-B"));
        bus.Subscribe<SecondEvent>(_ => order.Add("second-handler"));

        bus.Publish(new FirstEvent());

        Assert.Equal(["first-handler-A", "first-handler-B", "second-handler"], order);
    }
}
