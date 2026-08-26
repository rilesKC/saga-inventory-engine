using Inventory.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace Saga.Persistence.Tests;

public class S3ArchivingInventoryEventStoreTests
{
    private sealed class RecordingInventoryEventStore : IInventoryEventStore
    {
        public readonly List<(string Sku, int ExpectedEventCount, IReadOnlyList<object> Events)> Appended = [];

        public Task AppendRangeAsync(string sku, int expectedEventCount, IReadOnlyList<object> events, CancellationToken cancellationToken)
        {
            Appended.Add((sku, expectedEventCount, events));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<object>> LoadEventsAsync(string sku, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<object>>([]);
    }

    private sealed class ThrowingInventoryEventStore : IInventoryEventStore
    {
        public Task AppendRangeAsync(string sku, int expectedEventCount, IReadOnlyList<object> events, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated Mongo failure");

        public Task<IReadOnlyList<object>> LoadEventsAsync(string sku, CancellationToken cancellationToken) =>
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
    public async Task AppendRangeAsync_InnerStoreSucceeds_AlsoWritesToArchive()
    {
        var inner = new RecordingInventoryEventStore();
        var archive = new RecordingArchiveWriter();
        var store = new S3ArchivingInventoryEventStore(inner, archive, NullLogger<S3ArchivingInventoryEventStore>.Instance);
        var stockReserved = new StockReserved("SKU-1", "ORDER-1", 4, 199.99m);

        await store.AppendRangeAsync("SKU-1", 0, [stockReserved], CancellationToken.None);

        var innerCall = Assert.Single(inner.Appended);
        Assert.Equal("SKU-1", innerCall.Sku);
        Assert.Equal(0, innerCall.ExpectedEventCount);
        Assert.Equal([stockReserved], innerCall.Events);
        Assert.Single(archive.Written);
    }

    [Fact]
    public async Task AppendRangeAsync_ArchiveWriterThrows_StillSucceeds()
    {
        var inner = new RecordingInventoryEventStore();
        var archive = new ThrowingArchiveWriter();
        var store = new S3ArchivingInventoryEventStore(inner, archive, NullLogger<S3ArchivingInventoryEventStore>.Instance);

        await store.AppendRangeAsync("SKU-1", 0, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);

        Assert.Single(inner.Appended);
    }

    [Fact]
    public async Task AppendRangeAsync_InnerStoreThrows_ArchiveNeverCalledAndExceptionPropagates()
    {
        var inner = new ThrowingInventoryEventStore();
        var archive = new RecordingArchiveWriter();
        var store = new S3ArchivingInventoryEventStore(inner, archive, NullLogger<S3ArchivingInventoryEventStore>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.AppendRangeAsync("SKU-1", 0, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None));

        Assert.Empty(archive.Written);
    }

    [Fact]
    public async Task AppendRangeAsync_InnerStoreThrowsConcurrencyConflict_ArchiveNeverCalledAndExceptionPropagates()
    {
        var inner = new ThrowingConcurrencyInventoryEventStore();
        var archive = new RecordingArchiveWriter();
        var store = new S3ArchivingInventoryEventStore(inner, archive, NullLogger<S3ArchivingInventoryEventStore>.Instance);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => store.AppendRangeAsync("SKU-1", 0, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None));

        Assert.Empty(archive.Written);
    }

    private sealed class ThrowingConcurrencyInventoryEventStore : IInventoryEventStore
    {
        public Task AppendRangeAsync(string sku, int expectedEventCount, IReadOnlyList<object> events, CancellationToken cancellationToken) =>
            throw new ConcurrencyConflictException(sku, expectedEventCount, expectedEventCount + 1);

        public Task<IReadOnlyList<object>> LoadEventsAsync(string sku, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<object>>([]);
    }
}
