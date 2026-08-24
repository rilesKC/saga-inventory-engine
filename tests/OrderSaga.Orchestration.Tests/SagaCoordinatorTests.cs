using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Tests;

public class SagaCoordinatorTests
{
    [Fact]
    public void OnOrderPlaced_CreatesSagaStateAndPublishesReserveStockCommand()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(bus);
        ReserveStockCommand? published = null;
        bus.Subscribe<ReserveStockCommand>(e => published = e);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(published);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(4, published.Quantity);
        Assert.Equal(SagaStep.ReservingStock, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public void OnStockReservedReply_PublishesChargePaymentCommandWithSagaAmount()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(bus);
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));
        ChargePaymentCommand? published = null;
        bus.Subscribe<ChargePaymentCommand>(e => published = e);

        bus.Publish(new StockReservedReply("ORDER-1", "SKU-1"));

        Assert.NotNull(published);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(199.99m, published.Amount);
        Assert.Equal(SagaStep.AwaitingPayment, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public void OnStockReservationFailedReply_MarksSagaFailed()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(bus);
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        bus.Publish(new StockReservationFailedReply("ORDER-1", "SKU-1"));

        Assert.Equal(SagaStep.Failed, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public void OnPaymentChargedReply_PublishesConfirmReservationCommand()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(bus);
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));
        ConfirmReservationCommand? published = null;
        bus.Subscribe<ConfirmReservationCommand>(e => published = e);

        bus.Publish(new PaymentChargedReply("ORDER-1", "SKU-1"));

        Assert.NotNull(published);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(SagaStep.Confirming, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public void OnPaymentDeclinedReply_PublishesReleaseReservationCommand()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(bus);
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 999.99m));
        ReleaseReservationCommand? published = null;
        bus.Subscribe<ReleaseReservationCommand>(e => published = e);

        bus.Publish(new PaymentDeclinedReply("ORDER-1", "SKU-1"));

        Assert.NotNull(published);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(SagaStep.Compensating, coordinator.GetStep("ORDER-1"));
    }
}
