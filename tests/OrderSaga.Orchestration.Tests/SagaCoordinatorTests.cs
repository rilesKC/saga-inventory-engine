using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Tests;

public class SagaCoordinatorTests
{
    [Fact]
    public void OnOrderPlaced_CreatesSagaStateAndPublishesReserveStockCommand()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(bus);
        ReserveStockCommand? published = null;
        bus.Subscribe<ReserveStockCommand>(e => published = e);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(published);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(4, published.Quantity);
        Assert.Equal(SagaStep.ReservingStock, coordinator.GetStep("ORDER-1"));
    }
}
