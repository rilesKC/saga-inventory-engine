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
        // The raw SQS message body is the full EventBridge event structure (version, id,
        // detail-type, source, account, time, region, detail, ...), not our EventEnvelope
        // directly -- EventBridgeEventPublisher's Detail string gets re-embedded as the nested
        // "detail" object. Deserializing rawBody straight into EventEnvelope silently produced a
        // null MessageId (no matching top-level property), which DynamoDB then rejected as an
        // empty AttributeValue -- caught only by actually running this against LocalStack.
        using var document = JsonDocument.Parse(rawBody);
        var envelope = document.RootElement.GetProperty("detail").Deserialize<EventEnvelope>()
            ?? throw new InvalidOperationException("SQS message's EventBridge 'detail' did not deserialize to an EventEnvelope.");

        if (!_idempotencyStore.TryClaim(envelope.MessageId))
        {
            return;
        }

        var @event = EventTypeRegistry.Deserialize(envelope);
        _bus.Publish(@event);
    }
}
