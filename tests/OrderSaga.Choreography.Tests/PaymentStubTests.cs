using Inventory.Domain;

namespace OrderSaga.Choreography.Tests;

public class PaymentStubTests
{
    [Fact]
    public void OnStockReserved_AmountAtOrBelowThreshold_PublishesPaymentCharged()
    {
        var bus = new EventBus();
        _ = new PaymentStub(bus, 500m);
        PaymentCharged? published = null;
        bus.Subscribe<PaymentCharged>(e => published = e);
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        bus.Publish(new StockReserved("SKU-1", "ORDER-1", 4));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(199.99m, published.Amount);
    }

    [Fact]
    public void OnStockReserved_AmountAboveThreshold_PublishesPaymentDeclined()
    {
        var bus = new EventBus();
        _ = new PaymentStub(bus, 500m);
        PaymentDeclined? published = null;
        bus.Subscribe<PaymentDeclined>(e => published = e);
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 999.99m));

        bus.Publish(new StockReserved("SKU-1", "ORDER-1", 4));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(999.99m, published.Amount);
    }
}
