namespace OrderSaga.Choreography.Host;

public sealed record EventEnvelope(string MessageId, string EventType, string Payload);
