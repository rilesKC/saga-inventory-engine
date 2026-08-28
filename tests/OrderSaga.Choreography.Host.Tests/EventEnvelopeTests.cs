using System.Text.Json;
using Inventory.Domain;

namespace OrderSaga.Choreography.Host.Tests;

/// <summary>
/// Covers TraceContext's JSON shape only -- whether InsertDistributedTraceHeaders/
/// AcceptDistributedTraceHeaders actually populate/consume it correctly is New Relic-agent
/// runtime behavior, not testable without the real agent attached (same category as
/// MongoInventoryEventStore's Mongo-specific behavior: verified live, not unit-tested). This just
/// proves the wire format doesn't silently drop the field.
/// </summary>
public class EventEnvelopeTests
{
    [Fact]
    public void TraceContext_RoundTripsThroughJson()
    {
        var traceContext = new Dictionary<string, string> { ["traceparent"] = "00-abc-def-01" };
        var envelope = new EventEnvelope("MSG-1", "StockReserved", JsonSerializer.SerializeToElement(new { Sku = "SKU-1" }), traceContext);

        var json = JsonSerializer.Serialize(envelope);
        var roundTripped = JsonSerializer.Deserialize<EventEnvelope>(json);

        Assert.NotNull(roundTripped);
        Assert.NotNull(roundTripped.TraceContext);
        Assert.Equal("00-abc-def-01", roundTripped.TraceContext["traceparent"]);
    }

    [Fact]
    public void TraceContext_AbsentInJson_DeserializesToNull()
    {
        // A message published before TraceContext existed, or one whose producer's transaction
        // had nothing to insert, must still deserialize cleanly.
        var json = """{"MessageId":"MSG-1","EventType":"StockReserved","Payload":{"Sku":"SKU-1"}}""";

        var envelope = JsonSerializer.Deserialize<EventEnvelope>(json);

        Assert.NotNull(envelope);
        Assert.Null(envelope.TraceContext);
    }

    [Fact]
    public void Serialize_NoActiveNewRelicTransaction_TraceContextIsNull()
    {
        // This test process has no New Relic agent attached (no profiler, no [Transaction] scope)
        // -- InsertDistributedTraceHeaders's setter simply never gets invoked in that case (the
        // agent API is documented as a safe no-op when the agent isn't installed/active), so the
        // carrier stays empty. Serialize should collapse that to null rather than shipping an
        // empty-but-non-null TraceContext on every message.
        var envelope = EventTypeRegistry.Serialize(new StockReserved("SKU-1", "ORDER-1", 4, 199.99m));

        Assert.Null(envelope.TraceContext);
    }

    [Fact]
    public void Deserialize_EnvelopeWithFabricatedTraceContext_DoesNotThrow()
    {
        // Simulates a message a real, traced producer sent -- AcceptDistributedTraceHeaders must
        // handle a populated carrier safely even when this consumer process also has no agent
        // attached (same no-op-when-absent guarantee, just on the accept side).
        var original = EventTypeRegistry.Serialize(new StockReserved("SKU-1", "ORDER-1", 4, 199.99m));
        var withTraceContext = original with { TraceContext = new Dictionary<string, string> { ["traceparent"] = "00-abc-def-01" } };

        var exception = Record.Exception(() => EventTypeRegistry.Deserialize(withTraceContext));

        Assert.Null(exception);
    }
}
