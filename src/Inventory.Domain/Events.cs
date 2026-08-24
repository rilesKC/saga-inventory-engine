namespace Inventory.Domain;

public sealed record StockSeeded(string Sku, int InitialQuantity);

public sealed record StockReserved(string Sku, string OrderId, int Quantity);

public sealed record ReservationConfirmed(string Sku, string OrderId);

public sealed record ReservationReleased(string Sku, string OrderId);
