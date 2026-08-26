namespace OrderSaga.Orchestration.Tests;

public class CommandsAndRepliesTests
{
    [Fact]
    public void ReserveStockCommand_RecordsOrderIdSkuQuantityAndAmount()
    {
        var command = new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m);

        Assert.Equal("ORDER-1", command.OrderId);
        Assert.Equal("SKU-1", command.Sku);
        Assert.Equal(4, command.Quantity);
        Assert.Equal(199.99m, command.Amount);
    }
}
