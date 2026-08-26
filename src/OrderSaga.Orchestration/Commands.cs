namespace OrderSaga.Orchestration;

public sealed record ReserveStockCommand(string OrderId, string Sku, int Quantity, decimal Amount);

public sealed record ConfirmReservationCommand(string OrderId, string Sku);

public sealed record ReleaseReservationCommand(string OrderId, string Sku);

public sealed record ChargePaymentCommand(string OrderId, string Sku, decimal Amount);

public sealed record ScheduleShipmentCommand(string OrderId, string Sku);
