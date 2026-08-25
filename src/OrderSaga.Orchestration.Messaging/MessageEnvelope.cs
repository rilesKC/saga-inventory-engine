using System.Text.Json;

namespace OrderSaga.Orchestration.Messaging;

public sealed record MessageEnvelope(string MessageId, string MessageType, JsonElement Payload);
