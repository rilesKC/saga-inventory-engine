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
    private static async Task<InMemoryInventoryEventStore> SeedAsync(string sku, int quantity)
    {
        var eventStore = new InMemoryInventoryEventStore();
        await eventStore.AppendRangeAsync(sku, 0, [new StockSeeded(sku, quantity)], CancellationToken.None);
        return eventStore;
    }

    [Fact]
    public async Task Wire_OrderPlacedOnInbound_AppendsStockReservedAsPendingOutboxEntryWithoutLeakingOntoEitherBus()
    {
        // InventoryParticipant no longer publishes synchronously onto _outbound (see its
        // ApplyWithRetry doc comment) -- OutboxDrainerBackgroundService is what forwards a pending
        // entry onto the real transport in production. This test now covers that the wiring
        // doesn't leak the event onto either bus directly, and that it lands in the durable store
        // as pending -- not that it appears on outboundRaw synchronously.
        var inboundRaw = new EventBus();
        var outboundRaw = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        HostParticipantWiring.Wire(new InboundEventBus(inboundRaw), new OutboundEventBus(outboundRaw), paymentDeclineThreshold: 500m, eventStore);
        var leakedToOutbound = false;
        outboundRaw.Subscribe<StockReserved>(_ => leakedToOutbound = true);
        var leakedToInbound = false;
        inboundRaw.Subscribe<StockReserved>(_ => leakedToInbound = true);

        inboundRaw.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.False(leakedToOutbound);
        Assert.False(leakedToInbound);
        var pending = await eventStore.LoadUnpublishedAsync(CancellationToken.None);
        var stockReserved = Assert.IsType<StockReserved>(Assert.Single(pending, e => e.Event is StockReserved).Event);
        Assert.Equal("ORDER-1", stockReserved.OrderId);
    }

    [Fact]
    public async Task Wire_StockReservedOnInbound_PublishesPaymentChargedOnOutboundOnly()
    {
        var inboundRaw = new EventBus();
        var outboundRaw = new EventBus();
        HostParticipantWiring.Wire(new InboundEventBus(inboundRaw), new OutboundEventBus(outboundRaw), paymentDeclineThreshold: 500m, await SeedAsync("SKU-1", 10));
        PaymentCharged? onOutbound = null;
        outboundRaw.Subscribe<PaymentCharged>(e => onOutbound = e);
        var leakedToInbound = false;
        inboundRaw.Subscribe<PaymentCharged>(_ => leakedToInbound = true);
        inboundRaw.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        inboundRaw.Publish(new StockReserved("SKU-1", "ORDER-1", 4, 199.99m));

        Assert.NotNull(onOutbound);
        Assert.Equal("ORDER-1", onOutbound.OrderId);
        Assert.False(leakedToInbound);
    }

    [Fact]
    public async Task Wire_ReservationConfirmedOnInbound_PublishesShipmentScheduledOnOutboundOnly()
    {
        var inboundRaw = new EventBus();
        var outboundRaw = new EventBus();
        HostParticipantWiring.Wire(new InboundEventBus(inboundRaw), new OutboundEventBus(outboundRaw), paymentDeclineThreshold: 500m, await SeedAsync("SKU-1", 10));
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
