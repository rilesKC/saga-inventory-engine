using Inventory.Domain;

namespace OrderSaga.Choreography.Host;

public sealed record OrderDetails(string OrderId, string Sku, int Quantity, decimal Amount, string? Status, IReadOnlyList<OrderHistoryEntry> History);
