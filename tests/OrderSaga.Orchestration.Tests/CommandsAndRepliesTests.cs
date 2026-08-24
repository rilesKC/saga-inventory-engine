namespace OrderSaga.Orchestration.Tests;

public class CommandsAndRepliesTests
{
    [Fact]
    public void ReserveStockCommand_RecordsOrderIdSkuAndQuantity()
    {
        var command = new ReserveStockCommand("ORDER-1", "SKU-1", 4);

        Assert.Equal("ORDER-1", command.OrderId);
        Assert.Equal("SKU-1", command.Sku);
        Assert.Equal(4, command.Quantity);
    }
}
