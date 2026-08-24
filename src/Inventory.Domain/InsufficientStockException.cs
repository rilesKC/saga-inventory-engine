namespace Inventory.Domain;

public sealed class InsufficientStockException(string sku, int requestedQuantity, int availableQuantity)
    : Exception($"Cannot reserve {requestedQuantity} unit(s) of '{sku}': only {availableQuantity} available.")
{
    public string Sku { get; } = sku;
    public int RequestedQuantity { get; } = requestedQuantity;
    public int AvailableQuantity { get; } = availableQuantity;
}
