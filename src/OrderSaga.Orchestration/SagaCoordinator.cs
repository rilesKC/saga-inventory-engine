using OrderSaga.Shared;

namespace OrderSaga.Orchestration;

public sealed class SagaCoordinator
{
    private readonly InboundEventBus _inbound;
    private readonly OutboundEventBus _outbound;
    private readonly Dictionary<string, SagaState> _sagas = [];

    /// <param name="inbound">Subscribed to for the trigger event and every reply.</param>
    /// <param name="outbound">Published to for issued commands. In-process orchestration (this
    /// project's own tests) wraps the same underlying EventBus for both. The Host layer
    /// (saga-inventory-engine's AWS deployment) wraps two separate EventBus instances -- sharing
    /// one here would let this coordinator's own issued commands loop back through its own reply
    /// subscriptions in-process, the same class of bug choreography's InventoryParticipant/
    /// PaymentStub/ShippingStub were fixed for.</param>
    public SagaCoordinator(InboundEventBus inbound, OutboundEventBus outbound)
    {
        _inbound = inbound;
        _outbound = outbound;
        _inbound.Subscribe<OrderPlaced>(OnOrderPlaced);
        _inbound.Subscribe<StockReservedReply>(OnStockReservedReply);
        _inbound.Subscribe<StockReservationFailedReply>(OnStockReservationFailedReply);
        _inbound.Subscribe<PaymentChargedReply>(OnPaymentChargedReply);
        _inbound.Subscribe<PaymentDeclinedReply>(OnPaymentDeclinedReply);
        _inbound.Subscribe<ReservationConfirmedReply>(OnReservationConfirmedReply);
        _inbound.Subscribe<ReservationReleasedReply>(OnReservationReleasedReply);
        _inbound.Subscribe<ShipmentScheduledReply>(OnShipmentScheduledReply);
    }

    public SagaStep GetStep(string orderId) => _sagas[orderId].Step;

    private void OnOrderPlaced(OrderPlaced orderPlaced)
    {
        _sagas[orderPlaced.OrderId] = new SagaState(
            orderPlaced.OrderId, orderPlaced.Sku, orderPlaced.Quantity, orderPlaced.Amount, SagaStep.ReservingStock);

        _outbound.Publish(new ReserveStockCommand(orderPlaced.OrderId, orderPlaced.Sku, orderPlaced.Quantity));
    }

    private void OnStockReservedReply(StockReservedReply reply)
    {
        var saga = _sagas[reply.OrderId];
        _sagas[reply.OrderId] = saga with { Step = SagaStep.AwaitingPayment };
        _outbound.Publish(new ChargePaymentCommand(reply.OrderId, reply.Sku, saga.Amount));
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
        _outbound.Publish(new ConfirmReservationCommand(reply.OrderId, reply.Sku));
    }

    private void OnPaymentDeclinedReply(PaymentDeclinedReply reply)
    {
        var saga = _sagas[reply.OrderId];
        _sagas[reply.OrderId] = saga with { Step = SagaStep.Compensating };
        _outbound.Publish(new ReleaseReservationCommand(reply.OrderId, reply.Sku));
    }

    private void OnReservationConfirmedReply(ReservationConfirmedReply reply)
    {
        var saga = _sagas[reply.OrderId];
        _sagas[reply.OrderId] = saga with { Step = SagaStep.SchedulingShipment };
        _outbound.Publish(new ScheduleShipmentCommand(reply.OrderId, reply.Sku));
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
