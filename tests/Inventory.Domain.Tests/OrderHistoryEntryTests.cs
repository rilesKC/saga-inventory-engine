namespace Inventory.Domain.Tests;

public class OrderHistoryEntryTests
{
    [Fact]
    public void From_WrapsEventWithItsTypeName()
    {
        var stockReserved = new StockReserved("SKU-1", "ORDER-1", 4, 199.99m);

        var entry = OrderHistoryEntry.From(stockReserved);

        Assert.Equal("StockReserved", entry.EventType);
        Assert.Same(stockReserved, entry.Payload);
    }
}
