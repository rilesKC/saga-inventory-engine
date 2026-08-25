using System.Text.Json;

namespace OrderSaga.Choreography.Host;

public sealed record EventEnvelope(string MessageId, string EventType, JsonElement Payload);
