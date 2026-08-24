namespace OrderSaga.Choreography;

public sealed record StockReservationFailed(string OrderId, string Sku);

public sealed record PaymentCharged(string OrderId, string Sku, decimal Amount);

public sealed record PaymentDeclined(string OrderId, string Sku, decimal Amount);

public sealed record ShipmentScheduled(string OrderId, string Sku);
