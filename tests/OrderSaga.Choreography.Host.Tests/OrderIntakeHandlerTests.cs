using OrderSaga.Shared;

namespace OrderSaga.Choreography.Host.Tests;

public class OrderIntakeHandlerTests
{
    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public readonly List<object> Published = [];

        public void Publish(object @event) => Published.Add(@event);
    }

    [Fact]
    public void Handle_ValidRequest_PublishesOrderPlacedViaEventPublisher()
    {
        var publisher = new RecordingEventPublisher();
        var handler = new OrderIntakeHandler(publisher);
        var request = new PlaceOrderRequest("ORDER-1", "SKU-1", 4, 199.99m);

        handler.Handle(request);

        var published = Assert.IsType<OrderPlaced>(Assert.Single(publisher.Published));
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(4, published.Quantity);
        Assert.Equal(199.99m, published.Amount);
    }
}
