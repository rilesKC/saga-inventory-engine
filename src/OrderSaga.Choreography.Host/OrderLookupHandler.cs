using Inventory.Domain;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Logic behind the GET /orders/{id} endpoint, kept out of Program.cs for the same reason as
/// OrderIntakeHandler: testable without WebApplicationFactory. Choreography has no central
/// SagaState (nothing in this host's flow ever writes one -- see OrderStatusProjection), so an
/// order's identity/quantity/amount and status are all derived from its Inventory event history.
/// </summary>
public sealed class OrderLookupHandler
{
    private readonly IInventoryEventStore _store;

    public OrderLookupHandler(IInventoryEventStore store)
    {
        _store = store;
    }

    /// <returns>null if no events exist for this OrderId (caller should return 404).</returns>
    public async Task<OrderDetails?> HandleAsync(string orderId, CancellationToken cancellationToken)
    {
        var history = await _store.LoadEventsForOrderAsync(orderId, cancellationToken);
        var reserved = history.OfType<StockReserved>().FirstOrDefault();

        if (reserved is null)
        {
            return null;
        }

        var status = OrderStatusProjection.Project(history);
        var entries = history.Select(OrderHistoryEntry.From).ToList();
        return new OrderDetails(orderId, reserved.Sku, reserved.Quantity, reserved.Amount, status, entries);
    }
}
