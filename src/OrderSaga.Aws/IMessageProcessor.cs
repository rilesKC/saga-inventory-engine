namespace OrderSaga.Aws;

/// <summary>
/// The per-message logic (deserialize, claim-check, dispatch) that <see cref="SqsPollingBackgroundService"/>
/// wraps with the actual receive/delete loop. Choreography's and orchestration's implementations
/// deserialize genuinely differently (one unwraps an EventBridge envelope, the other reads a raw
/// SQS body directly) so they stay as two separate classes -- this interface is exactly the seam
/// that lets the receive/delete loop itself be shared without forcing that difference away.
/// </summary>
public interface IMessageProcessor
{
    Task ProcessMessageAsync(string rawBody, CancellationToken cancellationToken);
}
