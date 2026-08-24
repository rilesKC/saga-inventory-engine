using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Tests;

public class InventoryResponderTests
{
    [Fact]
    public void OnReserveStockCommand_WithSufficientStock_PublishesStockReservedReply()
    {
        var bus = new EventBus();
        var item = InventoryItem.Seed("SKU-1", 10);
        _ = new InventoryResponder(bus, new Dictionary<string, InventoryItem> { ["SKU-1"] = item });
        StockReservedReply? published = null;
        bus.Subscribe<StockReservedReply>(e => published = e);

        bus.Publish(new ReserveStockCommand("ORDER-1", "SKU-1", 4));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(6, item.AvailableQuantity);
    }

    [Fact]
    public void OnReserveStockCommand_WithInsufficientStock_PublishesStockReservationFailedReply()
    {
        var bus = new EventBus();
        var item = InventoryItem.Seed("SKU-1", 10);
        _ = new InventoryResponder(bus, new Dictionary<string, InventoryItem> { ["SKU-1"] = item });
        StockReservationFailedReply? published = null;
        bus.Subscribe<StockReservationFailedReply>(e => published = e);

        bus.Publish(new ReserveStockCommand("ORDER-1", "SKU-1", 11));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public void OnConfirmReservationCommand_ConfirmsReservationAndPublishesReservationConfirmedReply()
    {
        var bus = new EventBus();
        var item = InventoryItem.Seed("SKU-1", 10);
        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));
        _ = new InventoryResponder(bus, new Dictionary<string, InventoryItem> { ["SKU-1"] = item });
        ReservationConfirmedReply? published = null;
        bus.Subscribe<ReservationConfirmedReply>(e => published = e);

        bus.Publish(new ConfirmReservationCommand("ORDER-1", "SKU-1"));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(4, item.DeductedQuantity);
        Assert.Equal(0, item.ReservedQuantity);
    }
}
