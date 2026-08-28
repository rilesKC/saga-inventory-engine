using System.Text.Json;

namespace OrderSaga.Choreography.Host;

/// <param name="TraceContext">New Relic distributed-trace headers, inserted by
/// EventTypeRegistry.Serialize's producer-side transaction and accepted by
/// EventTypeRegistry.Deserialize on the consumer side -- the agent doesn't auto-instrument this
/// project's own EventBridge/SQS code the way it does HTTP calls, so this is how a trace survives
/// the hop. Null for a message published before this existed, or when there was no active
/// transaction to insert from.</param>
public sealed record EventEnvelope(string MessageId, string EventType, JsonElement Payload, IReadOnlyDictionary<string, string>? TraceContext = null);
