using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Tests;

public class OrderSagaChoreographyIntegrationTests
{
    private static (EventBus Bus, InventoryItem Item) WireSaga(string sku, int initialQuantity, decimal threshold)
    {
        var bus = new EventBus();
        var item = InventoryItem.Seed(sku, initialQuantity);
        _ = new InventoryParticipant(new InboundEventBus(bus), new OutboundEventBus(bus), new Dictionary<string, InventoryItem> { [sku] = item });
        _ = new PaymentStub(new InboundEventBus(bus), new OutboundEventBus(bus), threshold);
        _ = new ShippingStub(new InboundEventBus(bus), new OutboundEventBus(bus));
        return (bus, item);
    }

    [Fact]
    public void OrderPlaced_HappyPath_EndsWithShipmentScheduledAndReservationConfirmed()
    {
        var (bus, item) = WireSaga("SKU-1", 10, 500m);
        ShipmentScheduled? shipmentScheduled = null;
        bus.Subscribe<ShipmentScheduled>(e => shipmentScheduled = e);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(shipmentScheduled);
        Assert.Equal("ORDER-1", shipmentScheduled.OrderId);
        Assert.Equal(6, item.AvailableQuantity);
        Assert.Equal(4, item.DeductedQuantity);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void OrderPlaced_InsufficientStock_PublishesFailureAndNeverReachesPaymentOrShipping()
    {
        var (bus, item) = WireSaga("SKU-1", 10, 500m);
        StockReservationFailed? failed = null;
        bus.Subscribe<StockReservationFailed>(e => failed = e);
        var paymentOrShippingFired = false;
        bus.Subscribe<PaymentCharged>(_ => paymentOrShippingFired = true);
        bus.Subscribe<PaymentDeclined>(_ => paymentOrShippingFired = true);
        bus.Subscribe<ShipmentScheduled>(_ => paymentOrShippingFired = true);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 11, 199.99m));

        Assert.NotNull(failed);
        Assert.Equal("ORDER-1", failed.OrderId);
        Assert.False(paymentOrShippingFired);
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public void OrderPlaced_PaymentDeclined_ReleasesReservationAndNeverReachesShipping()
    {
        var (bus, item) = WireSaga("SKU-1", 10, 500m);
        ReservationReleased? released = null;
        bus.Subscribe<ReservationReleased>(e => released = e);
        var shippingFired = false;
        bus.Subscribe<ShipmentScheduled>(_ => shippingFired = true);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 999.99m));

        Assert.NotNull(released);
        Assert.Equal("ORDER-1", released.OrderId);
        Assert.False(shippingFired);
        Assert.Equal(10, item.AvailableQuantity);
    }
}
