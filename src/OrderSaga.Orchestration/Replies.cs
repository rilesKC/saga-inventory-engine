namespace OrderSaga.Orchestration;

public sealed record StockReservedReply(string OrderId, string Sku);

public sealed record StockReservationFailedReply(string OrderId, string Sku);

public sealed record ReservationConfirmedReply(string OrderId, string Sku);

public sealed record ReservationReleasedReply(string OrderId, string Sku);

public sealed record PaymentChargedReply(string OrderId, string Sku);

public sealed record PaymentDeclinedReply(string OrderId, string Sku);

public sealed record ShipmentScheduledReply(string OrderId, string Sku);
