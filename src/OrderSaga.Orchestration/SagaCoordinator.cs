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
    }

    public SagaStep GetStep(string orderId) => _sagas[orderId].Step;

    private void OnOrderPlaced(OrderPlaced orderPlaced)
    {
        _sagas[orderPlaced.OrderId] = new SagaState(
            orderPlaced.OrderId, orderPlaced.Sku, orderPlaced.Quantity, orderPlaced.Amount, SagaStep.ReservingStock);

        _bus.Publish(new ReserveStockCommand(orderPlaced.OrderId, orderPlaced.Sku, orderPlaced.Quantity));
    }
}
