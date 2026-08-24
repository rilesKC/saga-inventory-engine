using OrderSaga.Shared;

namespace OrderSaga.Choreography.Host.Tests;

public class EventEnvelopeTests
{
    [Fact]
    public void Serialize_OrderPlaced_ProducesEnvelopeWithEventTypeMessageIdAndPayload()
    {
        var orderPlaced = new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m);

        var envelope = EventTypeRegistry.Serialize(orderPlaced);

        Assert.Equal("OrderPlaced", envelope.EventType);
        Assert.False(string.IsNullOrWhiteSpace(envelope.MessageId));
        Assert.Contains("ORDER-1", envelope.Payload);
    }

    [Fact]
    public void Deserialize_EnvelopeWithOrderPlacedType_ReconstructsOriginalEvent()
    {
        var original = new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m);
        var envelope = EventTypeRegistry.Serialize(original);

        var reconstructed = EventTypeRegistry.Deserialize(envelope);

        var orderPlaced = Assert.IsType<OrderPlaced>(reconstructed);
        Assert.Equal(original, orderPlaced);
    }
}
