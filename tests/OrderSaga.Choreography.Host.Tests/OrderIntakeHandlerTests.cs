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
    public void Handle_ValidRequest_PublishesOrderPlacedViaEventPublisherAndReturnsTrue()
    {
        var publisher = new RecordingEventPublisher();
        var handler = new OrderIntakeHandler(publisher);
        var request = new PlaceOrderRequest("ORDER-1", "SKU-1", 4, 199.99m);

        var accepted = handler.Handle(request);

        Assert.True(accepted);
        var published = Assert.IsType<OrderPlaced>(Assert.Single(publisher.Published));
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(4, published.Quantity);
        Assert.Equal(199.99m, published.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Handle_NonPositiveQuantity_PublishesNothingAndReturnsFalse(int quantity)
    {
        // Rejecting here, before the message ever enters the pipeline, keeps a malformed request
        // from turning into a poison message deep in async processing: without this, the domain
        // layer's own rejection (InventoryItem.Handle(ReserveStock)) would throw uncaught through
        // OnOrderPlaced, and the message would be redelivered and fail forever.
        var publisher = new RecordingEventPublisher();
        var handler = new OrderIntakeHandler(publisher);
        var request = new PlaceOrderRequest("ORDER-1", "SKU-1", quantity, 199.99m);

        var accepted = handler.Handle(request);

        Assert.False(accepted);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public void Handle_NegativeAmount_PublishesNothingAndReturnsFalse()
    {
        var publisher = new RecordingEventPublisher();
        var handler = new OrderIntakeHandler(publisher);
        var request = new PlaceOrderRequest("ORDER-1", "SKU-1", 4, -199.99m);

        var accepted = handler.Handle(request);

        Assert.False(accepted);
        Assert.Empty(publisher.Published);
    }
}
