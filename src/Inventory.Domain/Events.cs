namespace Inventory.Domain;

/// <summary>
/// Marks an event type as meant to leave the process via the outbox drainer. StockSeeded
/// deliberately does not implement this -- it's appended directly by each Host's startup seeding
/// code (never through InventoryParticipant's command-handling path) and was never published
/// anywhere; it must not suddenly start going out just because it shares the same durable store.
/// </summary>
public interface IOutboundEvent;

public sealed record StockSeeded(string Sku, int InitialQuantity);

public sealed record StockReserved(string Sku, string OrderId, int Quantity, decimal Amount) : IOutboundEvent;

public sealed record ReservationConfirmed(string Sku, string OrderId, int Quantity) : IOutboundEvent;

public sealed record ReservationReleased(string Sku, string OrderId, int Quantity) : IOutboundEvent;
