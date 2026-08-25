using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Hosting;

namespace OrderSaga.Orchestration.Messaging;

/// <summary>
/// Sends a message to one specific queue, supplied at construction -- a Host wires up one instance
/// per destination queue (Coordinator needs two, one per command queue; Inventory and Responder each
/// need exactly one, always the coordinator-inbound queue). No independent logic worth unit-testing
/// in isolation; real send behavior is verified via LocalStack, same precedent as choreography's
/// EventBridgeEventPublisher.
/// </summary>
public sealed class SqsMessagePublisher : IMessagePublisher
{
    private readonly IAmazonSQS _client;
    private readonly string _queueUrl;
    private readonly CancellationToken _shutdownToken;

    public SqsMessagePublisher(IAmazonSQS client, string queueUrl, IHostApplicationLifetime lifetime)
    {
        _client = client;
        _queueUrl = queueUrl;

        // Same constraint as EventBridgeEventPublisher: this is invoked synchronously through
        // EventBus.Subscribe<T> (Action<T>, no async overload), so it can't be awaited up through
        // that chain without changing EventBus itself. ApplicationStopping at least lets a graceful
        // shutdown abort the call instead of blocking on it indefinitely.
        _shutdownToken = lifetime.ApplicationStopping;
    }

    public void Publish(object message)
    {
        var envelope = OrchestrationMessageTypeRegistry.Serialize(message);

        _client.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _queueUrl,
            MessageBody = System.Text.Json.JsonSerializer.Serialize(envelope),
        }, _shutdownToken).GetAwaiter().GetResult();
    }
}
