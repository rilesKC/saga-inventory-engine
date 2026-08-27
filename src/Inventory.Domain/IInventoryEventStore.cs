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

    /// <summary>
    /// Every event not yet marked published via <see cref="MarkPublishedAsync"/>, across all SKUs
    /// -- the outbox drainer's read side. Includes events whose type doesn't implement
    /// <see cref="IOutboundEvent"/> (e.g. StockSeeded): the store tracks "has this been marked
    /// published," uniformly, without judging which event types actually need to leave the
    /// process -- that's the drainer's job.
    /// </summary>
    Task<IReadOnlyList<PendingOutboxEntry>> LoadUnpublishedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Marks one event published so it no longer appears in <see cref="LoadUnpublishedAsync"/>.
    /// </summary>
    Task MarkPublishedAsync(string sku, int sequence, CancellationToken cancellationToken);
}

/// <summary>
/// One durably-stored, not-yet-published event -- Sequence is its position within its SKU's
/// history, the same numbering AppendRangeAsync's expectedEventCount already uses.
/// </summary>
public sealed record PendingOutboxEntry(string Sku, int Sequence, object Event);
