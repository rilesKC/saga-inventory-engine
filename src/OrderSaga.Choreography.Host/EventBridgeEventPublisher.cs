using Amazon.EventBridge;
using Amazon.EventBridge.Model;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Thin wrapper over <see cref="IAmazonEventBridge.PutEventsAsync"/> -- no independent logic
/// worth unit-testing in isolation. Real behavior (routing, delivery) verified via LocalStack.
/// </summary>
public sealed class EventBridgeEventPublisher : IEventPublisher
{
    private const string EventBusName = "order-saga-choreography";
    private const string Source = "order-saga-choreography.host";

    private readonly IAmazonEventBridge _client;

    public EventBridgeEventPublisher(IAmazonEventBridge client)
    {
        _client = client;
    }

    public void Publish(object @event)
    {
        var envelope = EventTypeRegistry.Serialize(@event);

        _client.PutEventsAsync(new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    EventBusName = EventBusName,
                    Source = Source,
                    DetailType = envelope.EventType,
                    Detail = System.Text.Json.JsonSerializer.Serialize(envelope),
                },
            ],
        }).GetAwaiter().GetResult();
    }
}
