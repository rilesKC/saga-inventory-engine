namespace Inventory.Domain.Tests;

public class InMemoryInventoryEventStoreTests
{
    [Fact]
    public async Task AppendRangeAsync_ThenLoadEventsAsync_ReturnsAppendedEventsInOrder()
    {
        var store = new InMemoryInventoryEventStore();
        var stockSeeded = new StockSeeded("SKU-1", 100);
        var stockReserved = new StockReserved("SKU-1", "ORDER-1", 4, 199.99m);

        await store.AppendRangeAsync("SKU-1", 0, [stockSeeded], CancellationToken.None);
        await store.AppendRangeAsync("SKU-1", 1, [stockReserved], CancellationToken.None);

        var events = await store.LoadEventsAsync("SKU-1", CancellationToken.None);
        Assert.Equal([stockSeeded, stockReserved], events);
    }

    [Fact]
    public async Task LoadEventsAsync_UnknownSku_ReturnsEmpty()
    {
        var store = new InMemoryInventoryEventStore();

        var events = await store.LoadEventsAsync("SKU-UNKNOWN", CancellationToken.None);

        Assert.Empty(events);
    }

    [Fact]
    public async Task AppendRangeAsync_DifferentSkus_AreIsolated()
    {
        var store = new InMemoryInventoryEventStore();
        var sku1Event = new StockSeeded("SKU-1", 100);
        var sku2Event = new StockSeeded("SKU-2", 50);

        await store.AppendRangeAsync("SKU-1", 0, [sku1Event], CancellationToken.None);
        await store.AppendRangeAsync("SKU-2", 0, [sku2Event], CancellationToken.None);

        Assert.Equal([sku1Event], await store.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal([sku2Event], await store.LoadEventsAsync("SKU-2", CancellationToken.None));
    }

    [Fact]
    public async Task AppendRangeAsync_EmptyList_IsANoOp()
    {
        var store = new InMemoryInventoryEventStore();

        await store.AppendRangeAsync("SKU-1", 0, [], CancellationToken.None);

        Assert.Empty(await store.LoadEventsAsync("SKU-1", CancellationToken.None));
    }

    [Fact]
    public async Task AppendRangeAsync_ExpectedCountMatchesActual_Succeeds()
    {
        var store = new InMemoryInventoryEventStore();
        await store.AppendRangeAsync("SKU-1", 0, [new StockSeeded("SKU-1", 100)], CancellationToken.None);

        await store.AppendRangeAsync("SKU-1", 1, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);

        Assert.Equal(2, (await store.LoadEventsAsync("SKU-1", CancellationToken.None)).Count);
    }

    [Fact]
    public async Task AppendRangeAsync_ExpectedCountStale_ThrowsConcurrencyConflict()
    {
        var store = new InMemoryInventoryEventStore();
        await store.AppendRangeAsync("SKU-1", 0, [new StockSeeded("SKU-1", 100)], CancellationToken.None);
        // A concurrent writer already appended a second event that this caller never saw.
        await store.AppendRangeAsync("SKU-1", 1, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            store.AppendRangeAsync("SKU-1", 1, [new StockReserved("SKU-1", "ORDER-2", 2, 99.99m)], CancellationToken.None));
    }
}
