using Inventory.Domain;

namespace OrderSaga.Choreography.Host.Tests;

public class OrderLookupHandlerTests
{
    [Fact]
    public async Task Handle_UnknownOrderId_ReturnsNull()
    {
        var store = new InMemoryInventoryEventStore();
        await store.AppendRangeAsync("SKU-1", 0, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);
        var handler = new OrderLookupHandler(store);

        var details = await handler.HandleAsync("ORDER-UNKNOWN", CancellationToken.None);

        Assert.Null(details);
    }

    [Fact]
    public async Task Handle_KnownOrderId_ReturnsDetailsWithHistoryAndStatus()
    {
        var store = new InMemoryInventoryEventStore();
        var reserved = new StockReserved("SKU-1", "ORDER-1", 4, 199.99m);
        var confirmed = new ReservationConfirmed("SKU-1", "ORDER-1", 4);
        await store.AppendRangeAsync("SKU-1", 0, [reserved, confirmed], CancellationToken.None);
        var handler = new OrderLookupHandler(store);

        var details = await handler.HandleAsync("ORDER-1", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("ORDER-1", details.OrderId);
        Assert.Equal("SKU-1", details.Sku);
        Assert.Equal(4, details.Quantity);
        Assert.Equal(199.99m, details.Amount);
        Assert.Equal("Confirmed", details.Status);
        Assert.Equal(
            [OrderHistoryEntry.From(reserved), OrderHistoryEntry.From(confirmed)],
            details.History);
    }
}
