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
        Assert.Equal("Confirmed", details.Status);
        Assert.Equal("Confirming", details.SagaStep);
        Assert.Equal(
            [OrderHistoryEntry.From(reserved), OrderHistoryEntry.From(confirmed)],
            details.History);
    }

    [Fact]
    public async Task Handle_StatusIsDerivedFromInventoryHistoryNotSagaStep()
    {
        // Status must use the same vocabulary Choreography's GET /orders/{id} reports (both derive
        // it from Inventory event history via OrderStatusProjection), so the two stacks agree on
        // what the same real-world state is called -- SagaStep's own vocabulary (ReservingStock,
        // AwaitingPayment, Confirming, SchedulingShipment, Completed, Compensating, Compensated,
        // Failed) is richer/orchestration-specific and reported separately, not as `status`.
        var sagaStateStore = new InMemorySagaStateStore();
        var inventoryEventStore = new InMemoryInventoryEventStore();
        var reserved = new StockReserved("SKU-1", "ORDER-1", 4, 199.99m);
        await inventoryEventStore.AppendRangeAsync("SKU-1", 0, [reserved], CancellationToken.None);
        // SchedulingShipment has no equivalent in Choreography's vocabulary -- only the Inventory
        // side (still just "Reserved" so far) is meant to drive `status`.
        var state = new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.SchedulingShipment);
        await sagaStateStore.SaveAsync(state, 0, CancellationToken.None);
        var handler = new OrderLookupHandler(sagaStateStore, inventoryEventStore);

        var details = await handler.HandleAsync("ORDER-1", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("Reserved", details.Status);
        Assert.Equal("SchedulingShipment", details.SagaStep);
    }
}
