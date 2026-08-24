using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Tests;

public class OrderSagaOrchestrationIntegrationTests
{
    private static (EventBus Bus, SagaCoordinator Coordinator, InventoryItem Item) WireSaga(
        string sku, int initialQuantity, decimal threshold)
    {
        var bus = new EventBus();
        var item = InventoryItem.Seed(sku, initialQuantity);
        var coordinator = new SagaCoordinator(bus);
        _ = new InventoryResponder(bus, new Dictionary<string, InventoryItem> { [sku] = item });
        _ = new PaymentResponder(bus, threshold);
        _ = new ShippingResponder(bus);
        return (bus, coordinator, item);
    }

    [Fact]
    public void OrderPlaced_HappyPath_EndsWithSagaCompletedAndReservationConfirmed()
    {
        var (bus, coordinator, item) = WireSaga("SKU-1", 10, 500m);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.Equal(SagaStep.Completed, coordinator.GetStep("ORDER-1"));
        Assert.Equal(6, item.AvailableQuantity);
        Assert.Equal(4, item.DeductedQuantity);
        Assert.Equal(0, item.ReservedQuantity);
    }
}
