using System.Collections.Concurrent;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Test double only -- doesn't survive process restart or share state across instances.
/// Production uses <see cref="DynamoDbIdempotencyStore"/>.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte> _claimed = new();

    public bool TryClaim(string messageId) => _claimed.TryAdd(messageId, 0);
}
