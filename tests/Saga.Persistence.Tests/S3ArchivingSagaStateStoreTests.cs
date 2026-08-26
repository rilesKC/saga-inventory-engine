using Microsoft.Extensions.Logging.Abstractions;
using OrderSaga.Orchestration;

namespace Saga.Persistence.Tests;

public class S3ArchivingSagaStateStoreTests
{
    private sealed class RecordingSagaStateStore : ISagaStateStore
    {
        public readonly List<SagaState> Saved = [];

        public Task SaveAsync(SagaState state, int expectedVersion, CancellationToken cancellationToken)
        {
            Saved.Add(state);
            return Task.CompletedTask;
        }

        public Task<SagaState?> TryLoadAsync(string orderId, CancellationToken cancellationToken) =>
            Task.FromResult<SagaState?>(null);

        public Task<IReadOnlyList<SagaState>> LoadAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SagaState>>([]);
    }

    private sealed class ThrowingSagaStateStore : ISagaStateStore
    {
        public Task SaveAsync(SagaState state, int expectedVersion, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated Mongo failure");

        public Task<SagaState?> TryLoadAsync(string orderId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated Mongo failure");

        public Task<IReadOnlyList<SagaState>> LoadAllAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated Mongo failure");
    }

    private sealed class RecordingArchiveWriter : IEventArchiveWriter
    {
        public readonly List<(string Key, string Payload)> Written = [];

        public Task PutAsync(string key, string payload, CancellationToken cancellationToken)
        {
            Written.Add((key, payload));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingArchiveWriter : IEventArchiveWriter
    {
        public Task PutAsync(string key, string payload, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated S3 failure");
    }

    [Fact]
    public async Task SaveAsync_InnerStoreSucceeds_AlsoWritesToArchive()
    {
        var inner = new RecordingSagaStateStore();
        var archive = new RecordingArchiveWriter();
        var store = new S3ArchivingSagaStateStore(inner, archive, NullLogger<S3ArchivingSagaStateStore>.Instance);
        var state = new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.ReservingStock, Version: 1);

        await store.SaveAsync(state, 0, CancellationToken.None);

        Assert.Equal([state], inner.Saved);
        Assert.Single(archive.Written);
    }

    [Fact]
    public async Task SaveAsync_ArchiveWriterThrows_StillSucceeds()
    {
        var inner = new RecordingSagaStateStore();
        var archive = new ThrowingArchiveWriter();
        var store = new S3ArchivingSagaStateStore(inner, archive, NullLogger<S3ArchivingSagaStateStore>.Instance);

        await store.SaveAsync(new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.ReservingStock, Version: 1), 0, CancellationToken.None);

        Assert.Single(inner.Saved);
    }

    [Fact]
    public async Task SaveAsync_InnerStoreThrows_ArchiveNeverCalledAndExceptionPropagates()
    {
        var inner = new ThrowingSagaStateStore();
        var archive = new RecordingArchiveWriter();
        var store = new S3ArchivingSagaStateStore(inner, archive, NullLogger<S3ArchivingSagaStateStore>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.ReservingStock, Version: 1), 0, CancellationToken.None));

        Assert.Empty(archive.Written);
    }

    [Fact]
    public async Task SaveAsync_InnerStoreThrowsConcurrencyConflict_ArchiveNeverCalledAndExceptionPropagates()
    {
        var inner = new ThrowingConcurrencySagaStateStore();
        var archive = new RecordingArchiveWriter();
        var store = new S3ArchivingSagaStateStore(inner, archive, NullLogger<S3ArchivingSagaStateStore>.Instance);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => store.SaveAsync(new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.ReservingStock, Version: 1), 0, CancellationToken.None));

        Assert.Empty(archive.Written);
    }

    private sealed class ThrowingConcurrencySagaStateStore : ISagaStateStore
    {
        public Task SaveAsync(SagaState state, int expectedVersion, CancellationToken cancellationToken) =>
            throw new ConcurrencyConflictException(state.OrderId, expectedVersion, expectedVersion + 1);

        public Task<SagaState?> TryLoadAsync(string orderId, CancellationToken cancellationToken) =>
            Task.FromResult<SagaState?>(null);

        public Task<IReadOnlyList<SagaState>> LoadAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SagaState>>([]);
    }
}
