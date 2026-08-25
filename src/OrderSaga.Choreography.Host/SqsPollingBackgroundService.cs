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
            try
            {
                var response = await _client.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = _queueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 20,
                }, stoppingToken);

                var processed = new List<DeleteMessageBatchRequestEntry>();

                foreach (var message in response.Messages ?? [])
                {
                    try
                    {
                        await _processor.ProcessMessageAsync(message.Body, stoppingToken);
                        processed.Add(new DeleteMessageBatchRequestEntry
                        {
                            Id = message.MessageId,
                            ReceiptHandle = message.ReceiptHandle,
                        });
                    }
                    catch (Exception ex)
                    {
                        // Deliberately not added to `processed`: letting the visibility timeout
                        // expire makes this message eligible for redelivery, eventually landing in
                        // the DLQ per the queue's redrive policy (Terraform task 12) if it keeps
                        // failing.
                        _logger.LogWarning(ex, "Failed to process SQS message {MessageId}; leaving for retry.", message.MessageId);
                    }
                }

                if (processed.Count > 0)
                {
                    var deleteResponse = await _client.DeleteMessageBatchAsync(new DeleteMessageBatchRequest
                    {
                        QueueUrl = _queueUrl,
                        Entries = processed,
                    }, stoppingToken);

                    foreach (var failed in deleteResponse.Failed)
                    {
                        // The event was already processed (claimed + published) at this point --
                        // a delete failure just means the message redelivers later and the claim
                        // makes the redelivery a no-op, not a data-loss or double-processing risk.
                        _logger.LogWarning("Failed to delete SQS message {MessageId} after successful processing: {Code}", failed.Id, failed.Code);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failure here (e.g. ReceiveMessageAsync itself, or an unexpected response
                // shape like the null Messages collection LocalStack returned on an empty poll)
                // must not take the whole host down -- HostOptions.BackgroundServiceExceptionBehavior
                // defaults to StopHost, and an unhandled exception escaping ExecuteAsync stops the
                // entire application, not just this loop iteration. Caught crashing the whole app
                // during LocalStack validation, not by any unit test.
                _logger.LogError(ex, "SQS poll failed; retrying after a short delay.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
