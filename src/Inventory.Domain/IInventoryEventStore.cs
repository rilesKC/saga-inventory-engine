namespace Inventory.Domain;

public interface IInventoryEventStore
{
    /// <summary>
    /// Appends events for a SKU, guarded by optimistic concurrency: expectedEventCount must match
    /// the number of events already stored for this SKU (what the caller's LoadEventsAsync call
    /// returned before it built the events being appended), or this throws
    /// <see cref="ConcurrencyConflictException"/> instead of appending -- another writer got there
    /// first. Callers must reload and retry, not swallow this.
    /// </summary>
    Task AppendRangeAsync(string sku, int expectedEventCount, IReadOnlyList<object> events, CancellationToken cancellationToken);

    Task<IReadOnlyList<object>> LoadEventsAsync(string sku, CancellationToken cancellationToken);
}
