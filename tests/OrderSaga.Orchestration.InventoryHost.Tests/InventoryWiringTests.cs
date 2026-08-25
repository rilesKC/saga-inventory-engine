using Inventory.Domain;
using OrderSaga.Orchestration;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.InventoryHost.Tests;

public class InventoryWiringTests
{
    [Fact]
    public void Wire_ReserveStockCommandOnInbound_PublishesStockReservedReplyOnOutboundOnly()
    {
        var inboundRaw = new EventBus();
        var outboundRaw = new EventBus();
        var item = InventoryItem.Seed("SKU-1", 10);
        InventoryWiring.Wire(new InboundEventBus(inboundRaw), new OutboundEventBus(outboundRaw), new Dictionary<string, InventoryItem> { ["SKU-1"] = item });
        StockReservedReply? onOutbound = null;
        outboundRaw.Subscribe<StockReservedReply>(e => onOutbound = e);
        var leakedToInbound = false;
        inboundRaw.Subscribe<StockReservedReply>(_ => leakedToInbound = true);

        inboundRaw.Publish(new ReserveStockCommand("ORDER-1", "SKU-1", 4));

        Assert.NotNull(onOutbound);
        Assert.Equal("ORDER-1", onOutbound.OrderId);
        Assert.False(leakedToInbound);
        Assert.Equal(6, item.AvailableQuantity);
    }
}
