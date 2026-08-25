using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Tests;

public class PaymentResponderTests
{
    [Fact]
    public void OnChargePaymentCommand_AmountAtOrBelowThreshold_PublishesPaymentChargedReply()
    {
        var bus = new EventBus();
        _ = new PaymentResponder(new InboundEventBus(bus), new OutboundEventBus(bus), 500m);
        PaymentChargedReply? published = null;
        bus.Subscribe<PaymentChargedReply>(e => published = e);

        bus.Publish(new ChargePaymentCommand("ORDER-1", "SKU-1", 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
    }

    [Fact]
    public void OnChargePaymentCommand_AmountAboveThreshold_PublishesPaymentDeclinedReply()
    {
        var bus = new EventBus();
        _ = new PaymentResponder(new InboundEventBus(bus), new OutboundEventBus(bus), 500m);
        PaymentDeclinedReply? published = null;
        bus.Subscribe<PaymentDeclinedReply>(e => published = e);

        bus.Publish(new ChargePaymentCommand("ORDER-1", "SKU-1", 999.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
    }
}
