using System.Text.Json;
using NewRelic.Api.Agent;
using OrderSaga.Aws;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Messaging;

/// <summary>
/// The real per-message logic (deserialize, claim-check, dispatch), decoupled from the actual SQS
/// receive/delete loop so it's independently unit-testable and reusable across every Host (the
/// thin AWS SDK plumbing that calls this lives in <see cref="OrderSaga.Aws.SqsPollingBackgroundService"/>,
/// shared with choreography, and is verified via LocalStack instead). Unlike choreography's
/// equivalent, there's no EventBridge envelope wrapping the raw SQS body -- Coordinator/Inventory/
/// Responder all send directly via <see cref="SqsMessagePublisher"/>, so the raw body IS the
/// serialized <see cref="MessageEnvelope"/>.
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

    // One New Relic transaction per SQS message processed -- gives
    // OrchestrationMessageTypeRegistry.Deserialize's AcceptDistributedTraceHeaders call something
    // to attach the producer's trace context to. Must sit on this concrete method, not
    // IMessageProcessor's interface member -- the [Transaction] attribute is documented as not
    // applying through an interface.
    [Transaction]
    public async Task<bool> ProcessMessageAsync(string rawBody, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<MessageEnvelope>(rawBody)
            ?? throw new InvalidOperationException("SQS message body did not deserialize to a MessageEnvelope.");

        if (!await _idempotencyStore.TryClaimAsync(envelope.MessageId, cancellationToken))
        {
            // Another delivery already holds (or held) this claim -- this delivery did nothing, so
            // the caller must not delete the SQS message on our account. See IMessageProcessor.
            return false;
        }

        try
        {
            var message = OrchestrationMessageTypeRegistry.Deserialize(envelope);

            // Publish itself stays synchronous -- see SqsMessagePublisher's constructor comment for
            // why the downstream AWS call it triggers can't be awaited through EventBus.Publish.
            _bus.Publish(message);
        }
        catch
        {
            // The claim was taken before dispatch specifically to stop two concurrent deliveries
            // from both passing TryClaim and double-processing. A failure here must release the
            // claim -- otherwise this message, deliberately left un-deleted so SQS can redeliver
            // it, finds the MessageId already claimed on retry and silently no-ops instead of
            // retrying, permanently dropping the message. See choreography's SqsMessageProcessor
            // for the incident this was fixed in response to.
            await _idempotencyStore.ReleaseAsync(envelope.MessageId, cancellationToken);
            throw;
        }

        return true;
    }
}
