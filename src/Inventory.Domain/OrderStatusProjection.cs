namespace Inventory.Domain;

/// <summary>
/// Derives an order's status from its Inventory event history. Shared by both saga stacks'
/// GET /orders/{id} handlers so the two stacks report the same status vocabulary for the same
/// real-world state -- Choreography has no central SagaState to read at all, and Orchestration
/// uses this instead of exposing its raw SagaStep as `status` (SagaStep is still available
/// separately as orchestration-only detail).
/// </summary>
public static class OrderStatusProjection
{
    public static string? Project(IReadOnlyList<object> events)
    {
        string? status = null;

        foreach (var @event in events)
        {
            status = @event switch
            {
                StockReserved => "Reserved",
                ReservationConfirmed => "Confirmed",
                ReservationReleased => "Released",
                _ => status,
            };
        }

        return status;
    }
}
