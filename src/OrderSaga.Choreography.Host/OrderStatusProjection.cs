using Inventory.Domain;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Choreography has no central SagaState to read (see OrderLookupHandler's doc comment), so an
/// order's status is synthesized here from the last matching Inventory event instead -- the same
/// events history already returned alongside this.
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
