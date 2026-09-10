namespace Inventory.Domain;

/// <summary>
/// Which Inventory events carry an OrderId, and how to read it -- shared so this domain knowledge
/// lives in exactly one place rather than being reimplemented per consumer.
/// <see cref="StockSeeded"/> never carries one; it's a per-SKU infra event with no order
/// association.
/// </summary>
public static class InventoryEventOrderId
{
    public static string? TryGet(object @event) => @event switch
    {
        StockReserved e => e.OrderId,
        ReservationConfirmed e => e.OrderId,
        ReservationReleased e => e.OrderId,
        _ => null,
    };
}
