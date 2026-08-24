using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Thin AWS SDK plumbing wrapping <see cref="SqsMessageProcessor"/> (already unit-tested) with the
/// actual receive/delete loop. Real behavior verified via LocalStack, not a unit test.
/// </summary>
public sealed class SqsPollingBackgroundService : BackgroundService
{
    private readonly IAmazonSQS _client;
    private readonly SqsMessageProcessor _processor;
    private readonly string _queueUrl;
    private readonly ILogger<SqsPollingBackgroundService> _logger;

    public SqsPollingBackgroundService(
        IAmazonSQS client,
        SqsMessageProcessor processor,
        string queueUrl,
        ILogger<SqsPollingBackgroundService> logger)
    {
        _client = client;
        _processor = processor;
        _queueUrl = queueUrl;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var response = await _client.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl,
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 20,
            }, stoppingToken);

            foreach (var message in response.Messages)
            {
                try
                {
                    _processor.ProcessMessage(message.Body);
                    await _client.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                }
                catch (Exception ex)
                {
                    // Deliberately not deleted: letting the visibility timeout expire makes this
                    // message eligible for redelivery, eventually landing in the DLQ per the
                    // queue's redrive policy (Terraform task 12) if it keeps failing.
                    _logger.LogWarning(ex, "Failed to process SQS message {MessageId}; leaving for retry.", message.MessageId);
                }
            }
        }
    }
}
