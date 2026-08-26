namespace Inventory.Domain.Tests;

public class EventsTests
{
    [Fact]
    public void StockReserved_RecordsSkuOrderIdQuantityAndAmount()
    {
        var @event = new StockReserved("SKU-1", "ORDER-1", 5, 199.99m);

        Assert.Equal("SKU-1", @event.Sku);
        Assert.Equal("ORDER-1", @event.OrderId);
        Assert.Equal(5, @event.Quantity);
        Assert.Equal(199.99m, @event.Amount);
    }
}
