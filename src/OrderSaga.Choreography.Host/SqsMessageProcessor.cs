using System.Text.Json;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// The real per-message logic (deserialize, claim-check, dispatch), decoupled from the actual SQS
/// receive/delete loop so it's independently unit-testable. The thin AWS SDK plumbing that calls
/// this lives in <see cref="SqsPollingBackgroundService"/> and is verified via LocalStack instead.
/// </summary>
public sealed class SqsMessageProcessor
{
    private readonly EventBus _bus;
    private readonly IIdempotencyStore _idempotencyStore;

    public SqsMessageProcessor(EventBus bus, IIdempotencyStore idempotencyStore)
    {
        _bus = bus;
        _idempotencyStore = idempotencyStore;
    }

    public void ProcessMessage(string rawBody)
    {
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(rawBody)
            ?? throw new InvalidOperationException("SQS message body did not deserialize to an EventEnvelope.");

        if (!_idempotencyStore.TryClaim(envelope.MessageId))
        {
            return;
        }

        var @event = EventTypeRegistry.Deserialize(envelope);
        _bus.Publish(@event);
    }
}
