using System.Text.Json;
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
        Assert.Contains("ORDER-1", envelope.Payload.GetRawText());
    }

    [Fact]
    public void Serialize_Payload_IsARealJsonObjectNotAnEscapedString()
    {
        // Payload used to be a pre-serialized string re-embedded (and re-escaped) inside the
        // envelope's own JSON on the wire -- a genuine double encode/decode on every event. It's a
        // JsonElement now specifically so it nests as a real object with a single encode pass.
        var envelope = EventTypeRegistry.Serialize(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.Equal(JsonValueKind.Object, envelope.Payload.ValueKind);
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
