namespace OrderSaga.Shared.Tests;

public class OrderPlacedTests
{
    [Fact]
    public void OrderPlaced_RecordsOrderIdSkuQuantityAndAmount()
    {
        var orderPlaced = new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m);

        Assert.Equal("ORDER-1", orderPlaced.OrderId);
        Assert.Equal("SKU-1", orderPlaced.Sku);
        Assert.Equal(4, orderPlaced.Quantity);
        Assert.Equal(199.99m, orderPlaced.Amount);
    }
}
