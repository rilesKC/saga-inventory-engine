namespace OrderSaga.Orchestration.CoordinatorHost;

public sealed record OrderDetails(string OrderId, string Sku, int Quantity, decimal Amount, string? Status, IReadOnlyList<object> History);
