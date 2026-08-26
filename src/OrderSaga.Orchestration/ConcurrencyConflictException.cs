namespace OrderSaga.Orchestration;

/// <summary>
/// Thrown by ISagaStateStore.SaveAsync when the caller's expectedVersion doesn't match the store's
/// actual current version for that OrderId -- another writer saved a newer version in the meantime.
/// Callers must reload the latest state and retry, not treat this as a fatal error. Same shape and
/// reasoning as Inventory.Domain's ConcurrencyConflictException, kept as a separate type since
/// SagaState and InventoryItem are otherwise unrelated aggregates -- see the Saga Persistence
/// spec's reasoning for why one is event-sourced and the other is a snapshot.
/// </summary>
public sealed class ConcurrencyConflictException(string orderId, int expectedVersion, int actualVersion)
    : Exception($"Concurrency conflict saving SagaState for '{orderId}': expected version {expectedVersion}, found {actualVersion}.")
{
    public string OrderId { get; } = orderId;
    public int ExpectedVersion { get; } = expectedVersion;
    public int ActualVersion { get; } = actualVersion;
}
