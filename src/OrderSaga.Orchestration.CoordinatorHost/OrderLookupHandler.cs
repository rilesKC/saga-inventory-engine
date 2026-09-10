using Inventory.Domain;
using OrderSaga.Orchestration;

namespace OrderSaga.Orchestration.CoordinatorHost;

/// <summary>
/// Logic behind the GET /orders/{id} endpoint, kept out of Program.cs for the same reason as
/// OrderIntakeHandler: testable without WebApplicationFactory. Orchestration has a central
/// SagaState, but its Step vocabulary (ReservingStock, AwaitingPayment, ...) doesn't match
/// Choreography's -- `Status` is derived from Inventory event history the same way Choreography
/// derives it (via OrderStatusProjection), so both stacks report status in one shared vocabulary;
/// SagaState.Step is still exposed separately as `SagaStep`, richer orchestration-only detail.
/// The inventory history itself needs filtering to this order, since LoadEventsAsync(sku) returns
/// every order's events for that SKU.
/// </summary>
public sealed class OrderLookupHandler
{
    private readonly ISagaStateStore _sagaStateStore;
    private readonly IInventoryEventStore _inventoryEventStore;

    public OrderLookupHandler(ISagaStateStore sagaStateStore, IInventoryEventStore inventoryEventStore)
    {
        _sagaStateStore = sagaStateStore;
        _inventoryEventStore = inventoryEventStore;
    }

    /// <returns>null if no SagaState exists for this OrderId (caller should return 404).</returns>
    public async Task<OrderDetails?> HandleAsync(string orderId, CancellationToken cancellationToken)
    {
        var state = await _sagaStateStore.TryLoadAsync(orderId, cancellationToken);
        if (state is null)
        {
            return null;
        }

        var skuEvents = await _inventoryEventStore.LoadEventsAsync(state.Sku, cancellationToken);
        var history = skuEvents.Where(e => InventoryEventOrderId.TryGet(e) == orderId).ToList();

        var status = OrderStatusProjection.Project(history);
        var entries = history.Select(OrderHistoryEntry.From).ToList();
        return new OrderDetails(orderId, state.Sku, state.Quantity, state.Amount, status, entries, state.Step.ToString());
    }
}
