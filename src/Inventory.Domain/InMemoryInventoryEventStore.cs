using System.Collections.Concurrent;

namespace Inventory.Domain;

/// <summary>
/// Test double only -- doesn't survive process restart or share state across instances.
/// Production uses a real Mongo-backed <see cref="IInventoryEventStore"/>.
/// </summary>
public sealed class InMemoryInventoryEventStore : IInventoryEventStore
{
    private sealed record StoredEvent(object Event, bool Published);

    private readonly ConcurrentDictionary<string, List<StoredEvent>> _eventsBySku = new();

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

            list.AddRange(events.Select(e => new StoredEvent(e, Published: false)));
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
            return Task.FromResult<IReadOnlyList<object>>(list.Select(e => e.Event).ToArray());
        }
    }

    public Task<IReadOnlyList<object>> LoadEventsForOrderAsync(string orderId, CancellationToken cancellationToken)
    {
        var matches = new List<object>();

        foreach (var list in _eventsBySku.Values)
        {
            lock (list)
            {
                matches.AddRange(list.Select(e => e.Event).Where(e => GetOrderId(e) == orderId));
            }
        }

        return Task.FromResult<IReadOnlyList<object>>(matches);
    }

    private static string? GetOrderId(object @event) => @event switch
    {
        StockReserved e => e.OrderId,
        ReservationConfirmed e => e.OrderId,
        ReservationReleased e => e.OrderId,
        _ => null,
    };

    public Task<IReadOnlyList<PendingOutboxEntry>> LoadUnpublishedAsync(CancellationToken cancellationToken)
    {
        var pending = new List<PendingOutboxEntry>();

        foreach (var (sku, list) in _eventsBySku)
        {
            lock (list)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (!list[i].Published)
                    {
                        pending.Add(new PendingOutboxEntry(sku, i, list[i].Event));
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<PendingOutboxEntry>>(pending);
    }

    public Task MarkPublishedAsync(string sku, int sequence, CancellationToken cancellationToken)
    {
        if (_eventsBySku.TryGetValue(sku, out var list))
        {
            lock (list)
            {
                list[sequence] = list[sequence] with { Published = true };
            }
        }

        return Task.CompletedTask;
    }
}
