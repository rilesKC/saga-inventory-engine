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

    [Fact]
    public void Serialize_NoActiveNewRelicTransaction_TraceContextIsNull()
    {
        // This test process has no New Relic agent attached (no profiler, no [Transaction] scope)
        // -- InsertDistributedTraceHeaders's setter simply never gets invoked in that case (the
        // agent API is documented as a safe no-op when the agent isn't installed/active), so the
        // carrier stays empty. Serialize should collapse that to null rather than shipping an
        // empty-but-non-null TraceContext on every message.
        var envelope = OrchestrationMessageTypeRegistry.Serialize(new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.Null(envelope.TraceContext);
    }

    [Fact]
    public void Deserialize_EnvelopeWithFabricatedTraceContext_DoesNotThrow()
    {
        // Simulates a message a real, traced producer sent -- AcceptDistributedTraceHeaders must
        // handle a populated carrier safely even when this consumer process also has no agent
        // attached (same no-op-when-absent guarantee, just on the accept side).
        var original = OrchestrationMessageTypeRegistry.Serialize(new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m));
        var withTraceContext = original with { TraceContext = new Dictionary<string, string> { ["traceparent"] = "00-abc-def-01" } };

        var exception = Record.Exception(() => OrchestrationMessageTypeRegistry.Deserialize(withTraceContext));

        Assert.Null(exception);
    }
}
