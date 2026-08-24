namespace OrderSaga.Shared;

public sealed record OrderPlaced(string OrderId, string Sku, int Quantity, decimal Amount);
