using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Tests;

public class PaymentStubTests
{
    [Fact]
    public void OnStockReserved_AmountAtOrBelowThreshold_PublishesPaymentCharged()
    {
        var bus = new EventBus();
        _ = new PaymentStub(new InboundEventBus(bus), new OutboundEventBus(bus), 500m);
        PaymentCharged? published = null;
        bus.Subscribe<PaymentCharged>(e => published = e);

        bus.Publish(new StockReserved("SKU-1", "ORDER-1", 4, 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(199.99m, published.Amount);
    }

    [Fact]
    public void OnStockReserved_AmountAboveThreshold_PublishesPaymentDeclined()
    {
        var bus = new EventBus();
        _ = new PaymentStub(new InboundEventBus(bus), new OutboundEventBus(bus), 500m);
        PaymentDeclined? published = null;
        bus.Subscribe<PaymentDeclined>(e => published = e);

        bus.Publish(new StockReserved("SKU-1", "ORDER-1", 4, 999.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(999.99m, published.Amount);
    }

    [Fact]
    public void OnStockReserved_NoPriorOrderPlacedSeenByThisInstance_StillResolvesAmountFromTheEventItself()
    {
        // Regression coverage for the multi-instance bug this fix removes: PaymentStub used to
        // cache Amount from OrderPlaced in an in-memory dictionary, so a StockReserved landing on
        // a task instance that never saw the matching OrderPlaced threw KeyNotFoundException.
        // StockReserved now carries Amount itself, so no prior event is needed at all.
        var bus = new EventBus();
        _ = new PaymentStub(new InboundEventBus(bus), new OutboundEventBus(bus), 500m);
        PaymentCharged? published = null;
        bus.Subscribe<PaymentCharged>(e => published = e);

        bus.Publish(new StockReserved("SKU-1", "ORDER-1", 4, 199.99m));

        Assert.NotNull(published);
        Assert.Equal(199.99m, published.Amount);
    }
}
