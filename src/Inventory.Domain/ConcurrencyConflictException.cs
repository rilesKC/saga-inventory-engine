namespace Inventory.Domain;

/// <summary>
/// Thrown by IInventoryEventStore.AppendRangeAsync when the caller's expectedEventCount doesn't
/// match the store's actual current event count for that SKU -- another writer appended events in
/// the meantime. Callers must reload the latest state and retry, not treat this as a fatal error.
/// </summary>
public sealed class ConcurrencyConflictException(string sku, int expectedEventCount, int actualEventCount)
    : Exception($"Concurrency conflict appending to '{sku}': expected {expectedEventCount} existing events, found {actualEventCount}.")
{
    public string Sku { get; } = sku;
    public int ExpectedEventCount { get; } = expectedEventCount;
    public int ActualEventCount { get; } = actualEventCount;
}
