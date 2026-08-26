namespace OrderSaga.Orchestration;

public interface ISagaStateStore
{
    /// <summary>
    /// Saves state, guarded by optimistic concurrency: expectedVersion must match the version
    /// currently stored for this OrderId (0 if no saga has been saved for it yet), or this throws
    /// <see cref="ConcurrencyConflictException"/> instead of saving -- another writer got there
    /// first. Callers must reload and retry, not swallow this.
    /// </summary>
    Task SaveAsync(SagaState state, int expectedVersion, CancellationToken cancellationToken);

    Task<SagaState?> TryLoadAsync(string orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SagaState>> LoadAllAsync(CancellationToken cancellationToken);
}
