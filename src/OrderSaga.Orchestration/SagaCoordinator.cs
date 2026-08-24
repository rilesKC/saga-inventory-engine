using OrderSaga.Shared;

namespace OrderSaga.Orchestration;

public sealed class SagaCoordinator
{
    private readonly EventBus _bus;
    private readonly Dictionary<string, SagaState> _sagas = [];

    public SagaCoordinator(EventBus bus)
    {
        _bus = bus;
        _bus.Subscribe<OrderPlaced>(OnOrderPlaced);
        _bus.Subscribe<StockReservedReply>(OnStockReservedReply);
        _bus.Subscribe<StockReservationFailedReply>(OnStockReservationFailedReply);
        _bus.Subscribe<PaymentChargedReply>(OnPaymentChargedReply);
        _bus.Subscribe<PaymentDeclinedReply>(OnPaymentDeclinedReply);
        _bus.Subscribe<ReservationConfirmedReply>(OnReservationConfirmedReply);
        _bus.Subscribe<ReservationReleasedReply>(OnReservationReleasedReply);
        _bus.Subscribe<ShipmentScheduledReply>(OnShipmentScheduledReply);
    }

    public SagaStep GetStep(string orderId) => _sagas[orderId].Step;

    private void OnOrderPlaced(OrderPlaced orderPlaced)
    {
        _sagas[orderPlaced.OrderId] = new SagaState(
            orderPlaced.OrderId, orderPlaced.Sku, orderPlaced.Quantity, orderPlaced.Amount, SagaStep.ReservingStock);

        _bus.Publish(new ReserveStockCommand(orderPlaced.OrderId, orderPlaced.Sku, orderPlaced.Quantity));
    }

    private void OnStockReservedReply(StockReservedReply reply)
    {
        var saga = _sagas[reply.OrderId];
        _sagas[reply.OrderId] = saga with { Step = SagaStep.AwaitingPayment };
        _bus.Publish(new ChargePaymentCommand(reply.OrderId, reply.Sku, saga.Amount));
    }

    private void OnStockReservationFailedReply(StockReservationFailedReply reply)
    {
        var saga = _sagas[reply.OrderId];
        _sagas[reply.OrderId] = saga with { Step = SagaStep.Failed };
    }

    private void OnPaymentChargedReply(PaymentChargedReply reply)
    {
        var saga = _sagas[reply.OrderId];
        _sagas[reply.OrderId] = saga with { Step = SagaStep.Confirming };
        _bus.Publish(new ConfirmReservationCommand(reply.OrderId, reply.Sku));
    }

    private void OnPaymentDeclinedReply(PaymentDeclinedReply reply)
    {
        var saga = _sagas[reply.OrderId];
        _sagas[reply.OrderId] = saga with { Step = SagaStep.Compensating };
        _bus.Publish(new ReleaseReservationCommand(reply.OrderId, reply.Sku));
    }

    private void OnReservationConfirmedReply(ReservationConfirmedReply reply)
    {
        var saga = _sagas[reply.OrderId];
        _sagas[reply.OrderId] = saga with { Step = SagaStep.SchedulingShipment };
        _bus.Publish(new ScheduleShipmentCommand(reply.OrderId, reply.Sku));
    }

    private void OnReservationReleasedReply(ReservationReleasedReply reply)
    {
        var saga = _sagas[reply.OrderId];
        _sagas[reply.OrderId] = saga with { Step = SagaStep.Compensated };
    }

    private void OnShipmentScheduledReply(ShipmentScheduledReply reply)
    {
        var saga = _sagas[reply.OrderId];
        _sagas[reply.OrderId] = saga with { Step = SagaStep.Completed };
    }
}
