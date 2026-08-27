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
    /// <summary>
    /// Returns true if this delivery genuinely processed (claimed and dispatched) the message --
    /// SqsPollingBackgroundService deletes the SQS message only when this is true. Returns false
    /// when another delivery already holds (or held) the idempotency claim: this delivery did
    /// nothing, so it must not delete a message a concurrent delivery may still be working on.
    /// </summary>
    Task<bool> ProcessMessageAsync(string rawBody, CancellationToken cancellationToken);
}
