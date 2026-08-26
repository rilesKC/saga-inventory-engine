using System.Collections.Concurrent;

namespace OrderSaga.Orchestration;

/// <summary>
/// Test double only -- doesn't survive process restart or share state across instances.
/// Production uses a real Mongo-backed <see cref="ISagaStateStore"/>.
/// </summary>
public sealed class InMemorySagaStateStore : ISagaStateStore
{
    private readonly ConcurrentDictionary<string, SagaState> _statesByOrderId = new();

    public Task SaveAsync(SagaState state, int expectedVersion, CancellationToken cancellationToken)
    {
        lock (_statesByOrderId)
        {
            var actualVersion = _statesByOrderId.TryGetValue(state.OrderId, out var existing) ? existing.Version : 0;
            if (actualVersion != expectedVersion)
            {
                throw new ConcurrencyConflictException(state.OrderId, expectedVersion, actualVersion);
            }

            _statesByOrderId[state.OrderId] = state;
        }

        return Task.CompletedTask;
    }

    public Task<SagaState?> TryLoadAsync(string orderId, CancellationToken cancellationToken) =>
        Task.FromResult(_statesByOrderId.TryGetValue(orderId, out var state) ? state : null);

    public Task<IReadOnlyList<SagaState>> LoadAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SagaState>>(_statesByOrderId.Values.ToArray());
}
