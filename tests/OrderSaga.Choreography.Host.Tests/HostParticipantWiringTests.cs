using Inventory.Domain;
using OrderSaga.Choreography;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Host.Tests;

/// <summary>
/// Covers the exact composition Program.cs performs at startup -- previously verified only by
/// running the whole host against LocalStack, never by a unit test. If a future change collapsed
/// inbound/outbound back onto one shared EventBus (the bug InboundEventBus/OutboundEventBus's
/// distinct types guard against at compile time), these tests would fail: a produced event would
/// leak back onto the inbound bus instead of staying confined to outbound.
/// </summary>
public class HostParticipantWiringTests
{
    private static Dictionary<string, InventoryItem> Items(string sku, int quantity) =>
        new() { [sku] = InventoryItem.Seed(sku, quantity) };

    [Fact]
    public void Wire_OrderPlacedOnInbound_PublishesStockReservedOnOutboundOnly()
    {
        var inboundRaw = new EventBus();
        var outboundRaw = new EventBus();
        HostParticipantWiring.Wire(new InboundEventBus(inboundRaw), new OutboundEventBus(outboundRaw), Items("SKU-1", 10), paymentDeclineThreshold: 500m);
        StockReserved? onOutbound = null;
        outboundRaw.Subscribe<StockReserved>(e => onOutbound = e);
        var leakedToInbound = false;
        inboundRaw.Subscribe<StockReserved>(_ => leakedToInbound = true);

        inboundRaw.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(onOutbound);
        Assert.Equal("ORDER-1", onOutbound.OrderId);
        Assert.False(leakedToInbound);
    }

    [Fact]
    public void Wire_StockReservedOnInbound_PublishesPaymentChargedOnOutboundOnly()
    {
        var inboundRaw = new EventBus();
        var outboundRaw = new EventBus();
        HostParticipantWiring.Wire(new InboundEventBus(inboundRaw), new OutboundEventBus(outboundRaw), Items("SKU-1", 10), paymentDeclineThreshold: 500m);
        PaymentCharged? onOutbound = null;
        outboundRaw.Subscribe<PaymentCharged>(e => onOutbound = e);
        var leakedToInbound = false;
        inboundRaw.Subscribe<PaymentCharged>(_ => leakedToInbound = true);
        inboundRaw.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        inboundRaw.Publish(new StockReserved("SKU-1", "ORDER-1", 4));

        Assert.NotNull(onOutbound);
        Assert.Equal("ORDER-1", onOutbound.OrderId);
        Assert.False(leakedToInbound);
    }

    [Fact]
    public void Wire_ReservationConfirmedOnInbound_PublishesShipmentScheduledOnOutboundOnly()
    {
        var inboundRaw = new EventBus();
        var outboundRaw = new EventBus();
        HostParticipantWiring.Wire(new InboundEventBus(inboundRaw), new OutboundEventBus(outboundRaw), Items("SKU-1", 10), paymentDeclineThreshold: 500m);
        ShipmentScheduled? onOutbound = null;
        outboundRaw.Subscribe<ShipmentScheduled>(e => onOutbound = e);
        var leakedToInbound = false;
        inboundRaw.Subscribe<ShipmentScheduled>(_ => leakedToInbound = true);

        inboundRaw.Publish(new ReservationConfirmed("SKU-1", "ORDER-1", 4));

        Assert.NotNull(onOutbound);
        Assert.Equal("ORDER-1", onOutbound.OrderId);
        Assert.False(leakedToInbound);
    }
}
