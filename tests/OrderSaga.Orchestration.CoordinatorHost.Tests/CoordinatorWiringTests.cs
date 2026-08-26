using OrderSaga.Orchestration;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.CoordinatorHost.Tests;

public class CoordinatorWiringTests
{
    [Fact]
    public void Wire_OrderPlacedOnInbound_PublishesReserveStockCommandOnOutboundOnly()
    {
        var inboundRaw = new EventBus();
        var outboundRaw = new EventBus();
        var coordinator = CoordinatorWiring.Wire(new InboundEventBus(inboundRaw), new OutboundEventBus(outboundRaw), new InMemorySagaStateStore());
        ReserveStockCommand? onOutbound = null;
        outboundRaw.Subscribe<ReserveStockCommand>(e => onOutbound = e);
        var leakedToInbound = false;
        inboundRaw.Subscribe<ReserveStockCommand>(_ => leakedToInbound = true);

        inboundRaw.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(onOutbound);
        Assert.Equal("ORDER-1", onOutbound.OrderId);
        Assert.False(leakedToInbound);
        Assert.Equal(SagaStep.ReservingStock, coordinator.GetStep("ORDER-1"));
    }
}
