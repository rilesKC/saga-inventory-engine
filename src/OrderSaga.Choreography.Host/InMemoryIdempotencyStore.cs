using System.Collections.Concurrent;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Test double only -- doesn't survive process restart or share state across instances.
/// Production uses <see cref="DynamoDbIdempotencyStore"/>.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte> _claimed = new();

    public Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken) =>
        Task.FromResult(_claimed.TryAdd(messageId, 0));

    public Task ReleaseAsync(string messageId, CancellationToken cancellationToken)
    {
        _claimed.TryRemove(messageId, out _);
        return Task.CompletedTask;
    }
}
