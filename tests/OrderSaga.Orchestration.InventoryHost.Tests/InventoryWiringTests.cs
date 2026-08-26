using Inventory.Domain;
using OrderSaga.Orchestration;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.InventoryHost.Tests;

public class InventoryWiringTests
{
    [Fact]
    public async Task Wire_ReserveStockCommandOnInbound_PublishesStockReservedReplyOnOutboundOnly()
    {
        var inboundRaw = new EventBus();
        var outboundRaw = new EventBus();
        var eventStore = new InMemoryInventoryEventStore();
        await eventStore.AppendRangeAsync("SKU-1", 0, [new StockSeeded("SKU-1", 10)], CancellationToken.None);
        InventoryWiring.Wire(new InboundEventBus(inboundRaw), new OutboundEventBus(outboundRaw), eventStore);
        StockReservedReply? onOutbound = null;
        outboundRaw.Subscribe<StockReservedReply>(e => onOutbound = e);
        var leakedToInbound = false;
        inboundRaw.Subscribe<StockReservedReply>(_ => leakedToInbound = true);

        inboundRaw.Publish(new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(onOutbound);
        Assert.Equal("ORDER-1", onOutbound.OrderId);
        Assert.False(leakedToInbound);
        var item = InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal(6, item.AvailableQuantity);
    }
}
