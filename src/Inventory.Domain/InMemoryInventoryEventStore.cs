using System.Collections.Concurrent;

namespace Inventory.Domain;

/// <summary>
/// Test double only -- doesn't survive process restart or share state across instances.
/// Production uses a real Mongo-backed <see cref="IInventoryEventStore"/>.
/// </summary>
public sealed class InMemoryInventoryEventStore : IInventoryEventStore
{
    private readonly ConcurrentDictionary<string, List<object>> _eventsBySku = new();

    public Task AppendRangeAsync(string sku, int expectedEventCount, IReadOnlyList<object> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return Task.CompletedTask;
        }

        var list = _eventsBySku.GetOrAdd(sku, _ => []);
        lock (list)
        {
            if (list.Count != expectedEventCount)
            {
                throw new ConcurrencyConflictException(sku, expectedEventCount, list.Count);
            }

            list.AddRange(events);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<object>> LoadEventsAsync(string sku, CancellationToken cancellationToken)
    {
        if (!_eventsBySku.TryGetValue(sku, out var list))
        {
            return Task.FromResult<IReadOnlyList<object>>([]);
        }

        lock (list)
        {
            return Task.FromResult<IReadOnlyList<object>>(list.ToArray());
        }
    }
}
