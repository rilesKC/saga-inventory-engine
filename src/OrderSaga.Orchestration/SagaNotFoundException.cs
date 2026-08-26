namespace OrderSaga.Orchestration;

public sealed class SagaNotFoundException(string orderId)
    : Exception($"No SagaState found for order '{orderId}'; cannot apply a transition to a saga that was never created or was lost.")
{
    public string OrderId { get; } = orderId;
}
