using Inventory.Domain;
using OrderSaga.Orchestration;

namespace OrderSaga.Orchestration.CoordinatorHost.Tests;

public class OrderLookupHandlerTests
{
    [Fact]
    public async Task Handle_UnknownOrderId_ReturnsNull()
    {
        var sagaStateStore = new InMemorySagaStateStore();
        var inventoryEventStore = new InMemoryInventoryEventStore();
        var handler = new OrderLookupHandler(sagaStateStore, inventoryEventStore);

        var details = await handler.HandleAsync("ORDER-UNKNOWN", CancellationToken.None);

        Assert.Null(details);
    }

    [Fact]
    public async Task Handle_KnownOrderId_ReturnsDetailsWithHistoryAndStatus()
    {
        var sagaStateStore = new InMemorySagaStateStore();
        var inventoryEventStore = new InMemoryInventoryEventStore();
        var reserved = new StockReserved("SKU-1", "ORDER-1", 4, 199.99m);
        var confirmed = new ReservationConfirmed("SKU-1", "ORDER-1", 4);
        await inventoryEventStore.AppendRangeAsync("SKU-1", 0, [reserved, confirmed], CancellationToken.None);
        var state = new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.Confirming);
        await sagaStateStore.SaveAsync(state, 0, CancellationToken.None);
        var handler = new OrderLookupHandler(sagaStateStore, inventoryEventStore);

        var details = await handler.HandleAsync("ORDER-1", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("ORDER-1", details.OrderId);
        Assert.Equal("SKU-1", details.Sku);
        Assert.Equal(4, details.Quantity);
        Assert.Equal(199.99m, details.Amount);
        Assert.Equal("Confirming", details.Status);
        Assert.Equal([reserved, confirmed], details.History);
    }
}
