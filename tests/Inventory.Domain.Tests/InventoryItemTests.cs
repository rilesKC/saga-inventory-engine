namespace Inventory.Domain.Tests;

public class InventoryItemTests
{
    [Fact]
    public void Seed_SetsAvailableQuantityToInitialQuantity()
    {
        var item = InventoryItem.Seed("SKU-1", 10);

        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public void Seed_EmitsStockSeededEvent()
    {
        var item = InventoryItem.Seed("SKU-1", 10);

        var @event = Assert.Single(item.UncommittedEvents);
        var stockSeeded = Assert.IsType<StockSeeded>(@event);
        Assert.Equal("SKU-1", stockSeeded.Sku);
        Assert.Equal(10, stockSeeded.InitialQuantity);
    }

    [Fact]
    public void ReserveStock_WithSufficientQuantity_EmitsStockReservedAndReducesAvailableQuantity()
    {
        var item = InventoryItem.Seed("SKU-1", 10);

        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));

        Assert.Equal(6, item.AvailableQuantity);
        var stockReserved = Assert.IsType<StockReserved>(item.UncommittedEvents[^1]);
        Assert.Equal("SKU-1", stockReserved.Sku);
        Assert.Equal("ORDER-1", stockReserved.OrderId);
        Assert.Equal(4, stockReserved.Quantity);
    }

    [Fact]
    public void ReserveStock_ExceedingAvailableQuantity_ThrowsInsufficientStockExceptionAndEmitsNoEvent()
    {
        var item = InventoryItem.Seed("SKU-1", 10);

        Assert.Throws<InsufficientStockException>(() =>
            item.Handle(new ReserveStock("SKU-1", "ORDER-1", 11)));

        Assert.Equal(10, item.AvailableQuantity);
        Assert.Single(item.UncommittedEvents);
    }

    [Fact]
    public void ReserveStock_DuplicateForSameOrderAndSku_ReturnsExistingReservationWithoutReducingAvailableAgain()
    {
        var item = InventoryItem.Seed("SKU-1", 10);
        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));

        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));

        Assert.Equal(6, item.AvailableQuantity);
        Assert.Equal(2, item.UncommittedEvents.Count);
    }
}
