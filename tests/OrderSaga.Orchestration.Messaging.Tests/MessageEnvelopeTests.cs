using System.Text.Json;
using OrderSaga.Orchestration;

namespace OrderSaga.Orchestration.Messaging.Tests;

public class MessageEnvelopeTests
{
    [Fact]
    public void Serialize_ReserveStockCommand_ProducesEnvelopeWithTypeMessageIdAndPayload()
    {
        var command = new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m);

        var envelope = OrchestrationMessageTypeRegistry.Serialize(command);

        Assert.Equal("ReserveStockCommand", envelope.MessageType);
        Assert.False(string.IsNullOrWhiteSpace(envelope.MessageId));
        Assert.Contains("ORDER-1", envelope.Payload.GetRawText());
    }

    [Fact]
    public void Deserialize_EnvelopeWithReserveStockCommandType_ReconstructsOriginalCommand()
    {
        var original = new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m);
        var envelope = OrchestrationMessageTypeRegistry.Serialize(original);

        var reconstructed = OrchestrationMessageTypeRegistry.Deserialize(envelope);

        var command = Assert.IsType<ReserveStockCommand>(reconstructed);
        Assert.Equal(original, command);
    }

    [Fact]
    public void Serialize_Payload_IsARealJsonObjectNotAnEscapedString()
    {
        var envelope = OrchestrationMessageTypeRegistry.Serialize(new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.Equal(JsonValueKind.Object, envelope.Payload.ValueKind);
    }
}
