namespace Inventory.Domain;

public sealed record ReserveStock(string Sku, string OrderId, int Quantity, decimal Amount);

public sealed record ConfirmReservation(string Sku, string OrderId);

public sealed record ReleaseReservation(string Sku, string OrderId);
