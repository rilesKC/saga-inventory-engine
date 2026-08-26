namespace OrderSaga.Orchestration;

public enum SagaStep
{
    ReservingStock,
    AwaitingPayment,
    Confirming,
    SchedulingShipment,
    Completed,
    Compensating,
    Compensated,
    Failed,
}

public sealed record SagaState(string OrderId, string Sku, int Quantity, decimal Amount, SagaStep Step, int Version = 0);
