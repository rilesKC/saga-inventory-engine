namespace Inventory.Domain;

public sealed class InvalidReservationRequestException(string sku, string orderId, string reason)
    : Exception($"Cannot reserve stock for order '{orderId}' on '{sku}': {reason}")
{
    public string Sku { get; } = sku;
    public string OrderId { get; } = orderId;
}
