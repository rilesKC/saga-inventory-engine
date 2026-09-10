using Inventory.Domain;
using OrderSaga.Orchestration;

namespace OrderSaga.Orchestration.CoordinatorHost;

/// <summary>
/// Logic behind the GET /orders/{id} endpoint, kept out of Program.cs for the same reason as
/// OrderIntakeHandler: testable without WebApplicationFactory. Unlike Choreography, orchestration
/// has a central SagaState carrying the order's current Step directly, so status doesn't need
/// synthesizing from events -- but the inventory history still needs filtering to this order,
/// since LoadEventsAsync(sku) returns every order's events for that SKU.
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
        var history = skuEvents.Where(e => GetOrderId(e) == orderId).ToList();

        return new OrderDetails(orderId, state.Sku, state.Quantity, state.Amount, state.Step.ToString(), history);
    }

    private static string? GetOrderId(object @event) => @event switch
    {
        StockReserved e => e.OrderId,
        ReservationConfirmed e => e.OrderId,
        ReservationReleased e => e.OrderId,
        _ => null,
    };
}
