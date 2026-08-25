using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Microsoft.Extensions.Hosting;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Thin wrapper over <see cref="IAmazonEventBridge.PutEventsAsync"/>. Still no unit test double for
/// the AWS SDK client itself (IAmazonEventBridge's surface is too large to hand-roll without a
/// mocking framework, which this repo doesn't use) -- routing/delivery and the FailedEntryCount
/// check below are verified via LocalStack instead.
/// </summary>
public sealed class EventBridgeEventPublisher : IEventPublisher
{
    private const string Source = "order-saga-choreography.host";

    private readonly IAmazonEventBridge _client;
    private readonly string _eventBusName;
    private readonly CancellationToken _shutdownToken;

    public EventBridgeEventPublisher(IAmazonEventBridge client, string eventBusName, IHostApplicationLifetime lifetime)
    {
        _client = client;
        _eventBusName = eventBusName;

        // OutboundEventForwarder subscribes this method directly to an EventBus (Action<T>, no
        // async overload -- see EventBus.Subscribe), so this call can't be awaited up through that
        // chain without changing EventBus itself, which is shared, already-tested infrastructure
        // also used by the Orchestration saga. ApplicationStopping is the closest thing available
        // at the point this class is constructed (DI, before any BackgroundService's own
        // per-request stoppingToken exists) to still let a graceful shutdown abort this call rather
        // than block on it indefinitely.
        _shutdownToken = lifetime.ApplicationStopping;
    }

    public void Publish(object @event)
    {
        var envelope = EventTypeRegistry.Serialize(@event);

        var response = _client.PutEventsAsync(new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    EventBusName = _eventBusName,
                    Source = Source,
                    DetailType = envelope.EventType,
                    Detail = System.Text.Json.JsonSerializer.Serialize(envelope),
                },
            ],
        }, _shutdownToken).GetAwaiter().GetResult();

        // PutEvents returns 200 even when an individual entry fails (throttling, transient AWS
        // error) -- FailedEntryCount/per-entry ErrorCode is the only signal. Left unchecked, a
        // throttled publish looked identical to success and the event silently vanished.
        if (response.FailedEntryCount > 0)
        {
            var failure = response.Entries[0];
            throw new InvalidOperationException(
                $"EventBridge PutEvents failed for {envelope.EventType} (MessageId {envelope.MessageId}): {failure.ErrorCode} - {failure.ErrorMessage}");
        }
    }
}
