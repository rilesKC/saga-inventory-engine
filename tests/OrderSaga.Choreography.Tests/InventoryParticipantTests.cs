using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Tests;

public class InventoryParticipantTests
{
    [Fact]
    public void OnOrderPlaced_WithSufficientStock_PublishesStockReserved()
    {
        var bus = new EventBus();
        var item = InventoryItem.Seed("SKU-1", 10);
        _ = new InventoryParticipant(bus, bus, new Dictionary<string, InventoryItem> { ["SKU-1"] = item });
        StockReserved? published = null;
        bus.Subscribe<StockReserved>(e => published = e);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(4, published.Quantity);
        Assert.Equal(6, item.AvailableQuantity);
    }

    [Fact]
    public void OnOrderPlaced_WithInsufficientStock_PublishesStockReservationFailed()
    {
        var bus = new EventBus();
        var item = InventoryItem.Seed("SKU-1", 10);
        _ = new InventoryParticipant(bus, bus, new Dictionary<string, InventoryItem> { ["SKU-1"] = item });
        StockReservationFailed? published = null;
        bus.Subscribe<StockReservationFailed>(e => published = e);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 11, 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public void OnPaymentCharged_ConfirmsReservationAndPublishesReservationConfirmed()
    {
        var bus = new EventBus();
        var item = InventoryItem.Seed("SKU-1", 10);
        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));
        _ = new InventoryParticipant(bus, bus, new Dictionary<string, InventoryItem> { ["SKU-1"] = item });
        ReservationConfirmed? published = null;
        bus.Subscribe<ReservationConfirmed>(e => published = e);

        bus.Publish(new PaymentCharged("ORDER-1", "SKU-1", 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(4, item.DeductedQuantity);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void OnPaymentDeclined_ReleasesReservationAndPublishesReservationReleased()
    {
        var bus = new EventBus();
        var item = InventoryItem.Seed("SKU-1", 10);
        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));
        _ = new InventoryParticipant(bus, bus, new Dictionary<string, InventoryItem> { ["SKU-1"] = item });
        ReservationReleased? published = null;
        bus.Subscribe<ReservationReleased>(e => published = e);

        bus.Publish(new PaymentDeclined("ORDER-1", "SKU-1", 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(10, item.AvailableQuantity);
    }
}
