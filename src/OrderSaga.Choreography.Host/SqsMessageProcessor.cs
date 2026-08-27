using System.Text.Json;
using OrderSaga.Aws;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// The real per-message logic (deserialize, claim-check, dispatch), decoupled from the actual SQS
/// receive/delete loop so it's independently unit-testable. The thin AWS SDK plumbing that calls
/// this (<see cref="OrderSaga.Aws.SqsPollingBackgroundService"/>, shared with orchestration) is
/// verified via LocalStack instead.
/// </summary>
public sealed class SqsMessageProcessor : IMessageProcessor
{
    private readonly EventBus _bus;
    private readonly IIdempotencyStore _idempotencyStore;

    public SqsMessageProcessor(EventBus bus, IIdempotencyStore idempotencyStore)
    {
        _bus = bus;
        _idempotencyStore = idempotencyStore;
    }

    public async Task<bool> ProcessMessageAsync(string rawBody, CancellationToken cancellationToken)
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

        if (!await _idempotencyStore.TryClaimAsync(envelope.MessageId, cancellationToken))
        {
            // Another delivery already holds (or held) this claim -- this delivery did nothing, so
            // the caller must not delete the SQS message on our account. See IMessageProcessor.
            return false;
        }

        try
        {
            var @event = EventTypeRegistry.Deserialize(envelope);

            // Publish itself stays synchronous -- it dispatches straight through participants'
            // handlers (EventBus.Subscribe only takes Action<T>), which is why the idempotency
            // claim above is the only genuinely awaitable I/O left in this method; the AWS call
            // downstream of Publish (EventBridgeEventPublisher) can't be awaited without changing
            // EventBus itself. See EventBridgeEventPublisher's constructor comment.
            _bus.Publish(@event);
        }
        catch
        {
            // The claim was taken before dispatch specifically to stop two concurrent deliveries
            // from both passing TryClaim and double-processing. But that means a failure here (a
            // participant's outbound EventBridge publish throwing, for example) must release the
            // claim -- otherwise this message, deliberately left un-deleted so SQS can redeliver
            // it, finds the MessageId already claimed on retry and silently no-ops instead of
            // retrying, permanently dropping the event.
            await _idempotencyStore.ReleaseAsync(envelope.MessageId, cancellationToken);
            throw;
        }

        return true;
    }
}
