namespace Inventory.Domain;

public sealed class InvalidReservationStateException(string sku, string orderId, string attemptedTransition)
    : Exception($"Cannot apply '{attemptedTransition}' to reservation for order '{orderId}' on '{sku}': it's not in a state that allows this transition.")
{
    public string Sku { get; } = sku;
    public string OrderId { get; } = orderId;
    public string AttemptedTransition { get; } = attemptedTransition;
}
