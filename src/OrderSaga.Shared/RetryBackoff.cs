namespace OrderSaga.Shared;

/// <summary>
/// Bounded retry-with-backoff for the reload-mutate-save loops InventoryParticipant/
/// InventoryResponder/SagaCoordinator each run against their own durable store on an optimistic-
/// concurrency conflict. Shared so all three use one tuned cap/backoff instead of three
/// independently-drifting copies of the same magic numbers -- the retry loops themselves stay
/// separate per store type, only this "how long to wait, how many times to try" policy is common.
/// </summary>
public static class RetryBackoff
{
    /// <summary>
    /// Total attempts allowed, including the first. Sustained contention (every attempt conflicts)
    /// must give up and let the caller's own failure path handle it -- for these call sites, that
    /// means propagating out to SqsMessageProcessor, which leaves the message for SQS redelivery
    /// rather than spinning this thread forever.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Blocking sleep before retrying. `attempt` is 1-based: pass 1 after the first failure, 2
    /// after the second, and so on. Exponential base delay with random jitter, so two contending
    /// writers retrying the same conflict don't stay in lockstep and keep re-colliding.
    /// </summary>
    public static void WaitBeforeRetry(int attempt)
    {
        var baseDelayMs = Math.Min(20 * (1 << (attempt - 1)), 500);
        var jitteredMs = baseDelayMs + Random.Shared.Next(0, baseDelayMs);
        Thread.Sleep(jitteredMs);
    }
}
