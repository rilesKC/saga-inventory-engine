namespace Inventory.Domain;

/// <summary>
/// One event in an order's history, wrapped with an explicit type discriminator -- the raw event
/// records (StockReserved etc.) don't self-describe their type when JSON-serialized, so a caller
/// reading the HTTP response has no way to tell them apart without this.
/// </summary>
public sealed record OrderHistoryEntry(string EventType, object Payload)
{
    public static OrderHistoryEntry From(object @event) => new(@event.GetType().Name, @event);
}
