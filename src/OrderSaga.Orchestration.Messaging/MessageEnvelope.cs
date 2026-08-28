using System.Text.Json;

namespace OrderSaga.Orchestration.Messaging;

/// <param name="TraceContext">New Relic distributed-trace headers, inserted by
/// OrchestrationMessageTypeRegistry.Serialize's producer-side transaction and accepted by
/// OrchestrationMessageTypeRegistry.Deserialize on the consumer side -- the agent doesn't
/// auto-instrument this project's own EventBus/SQS polling code the way it does HTTP calls, so
/// this is how a trace survives the hop. Null for a message published before this existed, or
/// when there was no active transaction to insert from.</param>
public sealed record MessageEnvelope(string MessageId, string MessageType, JsonElement Payload, IReadOnlyDictionary<string, string>? TraceContext = null);
