using OrderSaga.Orchestration;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.ResponderHost.Tests;

public class ResponderWiringTests
{
    [Fact]
    public void Wire_ChargePaymentCommandOnInbound_PublishesPaymentReplyOnOutboundOnly()
    {
        var inboundRaw = new EventBus();
        var outboundRaw = new EventBus();
        ResponderWiring.Wire(new InboundEventBus(inboundRaw), new OutboundEventBus(outboundRaw), paymentDeclineThreshold: 500m);
        PaymentChargedReply? onOutbound = null;
        outboundRaw.Subscribe<PaymentChargedReply>(e => onOutbound = e);
        var leakedToInbound = false;
        inboundRaw.Subscribe<PaymentChargedReply>(_ => leakedToInbound = true);

        inboundRaw.Publish(new ChargePaymentCommand("ORDER-1", "SKU-1", 199.99m));

        Assert.NotNull(onOutbound);
        Assert.Equal("ORDER-1", onOutbound.OrderId);
        Assert.False(leakedToInbound);
    }

    [Fact]
    public void Wire_ScheduleShipmentCommandOnInbound_PublishesShipmentScheduledReplyOnOutboundOnly()
    {
        var inboundRaw = new EventBus();
        var outboundRaw = new EventBus();
        ResponderWiring.Wire(new InboundEventBus(inboundRaw), new OutboundEventBus(outboundRaw), paymentDeclineThreshold: 500m);
        ShipmentScheduledReply? onOutbound = null;
        outboundRaw.Subscribe<ShipmentScheduledReply>(e => onOutbound = e);
        var leakedToInbound = false;
        inboundRaw.Subscribe<ShipmentScheduledReply>(_ => leakedToInbound = true);

        inboundRaw.Publish(new ScheduleShipmentCommand("ORDER-1", "SKU-1"));

        Assert.NotNull(onOutbound);
        Assert.Equal("ORDER-1", onOutbound.OrderId);
        Assert.False(leakedToInbound);
    }
}
