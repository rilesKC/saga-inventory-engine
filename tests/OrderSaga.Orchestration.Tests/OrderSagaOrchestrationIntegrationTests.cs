using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Tests;

public class OrderSagaOrchestrationIntegrationTests
{
    private static async Task<(EventBus Bus, SagaCoordinator Coordinator, InMemoryInventoryEventStore EventStore)> WireSagaAsync(
        string sku, int initialQuantity, decimal threshold)
    {
        var bus = new EventBus();
        var eventStore = new InMemoryInventoryEventStore();
        await eventStore.AppendRangeAsync(sku, 0, [new StockSeeded(sku, initialQuantity)], CancellationToken.None);
        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), new InMemorySagaStateStore());
        _ = new InventoryResponder(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);
        _ = new PaymentResponder(new InboundEventBus(bus), new OutboundEventBus(bus), threshold);
        _ = new ShippingResponder(new InboundEventBus(bus), new OutboundEventBus(bus));
        return (bus, coordinator, eventStore);
    }

    private static async Task<InventoryItem> LoadItemAsync(InMemoryInventoryEventStore eventStore, string sku) =>
        InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync(sku, CancellationToken.None));

    [Fact]
    public async Task OrderPlaced_HappyPath_EndsWithSagaCompletedAndReservationConfirmed()
    {
        var (bus, coordinator, eventStore) = await WireSagaAsync("SKU-1", 10, 500m);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.Equal(SagaStep.Completed, coordinator.GetStep("ORDER-1"));
        var item = await LoadItemAsync(eventStore, "SKU-1");
        Assert.Equal(6, item.AvailableQuantity);
        Assert.Equal(4, item.DeductedQuantity);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public async Task OrderPlaced_InsufficientStock_MarksSagaFailedAndNeverReachesPaymentOrShipping()
    {
        var (bus, coordinator, eventStore) = await WireSagaAsync("SKU-1", 10, 500m);
        var paymentOrShippingFired = false;
        bus.Subscribe<PaymentChargedReply>(_ => paymentOrShippingFired = true);
        bus.Subscribe<PaymentDeclinedReply>(_ => paymentOrShippingFired = true);
        bus.Subscribe<ShipmentScheduledReply>(_ => paymentOrShippingFired = true);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 11, 199.99m));

        Assert.Equal(SagaStep.Failed, coordinator.GetStep("ORDER-1"));
        Assert.False(paymentOrShippingFired);
        var item = await LoadItemAsync(eventStore, "SKU-1");
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public async Task OrderPlaced_PaymentDeclined_MarksSagaCompensatedAndNeverReachesShipping()
    {
        var (bus, coordinator, eventStore) = await WireSagaAsync("SKU-1", 10, 500m);
        var shippingFired = false;
        bus.Subscribe<ShipmentScheduledReply>(_ => shippingFired = true);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 999.99m));

        Assert.Equal(SagaStep.Compensated, coordinator.GetStep("ORDER-1"));
        Assert.False(shippingFired);
        var item = await LoadItemAsync(eventStore, "SKU-1");
        Assert.Equal(10, item.AvailableQuantity);
    }
}
