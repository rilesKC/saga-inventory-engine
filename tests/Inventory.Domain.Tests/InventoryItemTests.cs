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
}
