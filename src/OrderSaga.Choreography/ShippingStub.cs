using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Choreography;

public sealed class ShippingStub
{
    private readonly EventBus _inbound;
    private readonly EventBus _outbound;

    /// <param name="inbound">Subscribed to for trigger events.</param>
    /// <param name="outbound">Published to for produced events. See
    /// <see cref="InventoryParticipant"/>'s constructor for why these are separate.</param>
    public ShippingStub(EventBus inbound, EventBus outbound)
    {
        _inbound = inbound;
        _outbound = outbound;
        _inbound.Subscribe<ReservationConfirmed>(OnReservationConfirmed);
    }

    private void OnReservationConfirmed(ReservationConfirmed reservationConfirmed)
    {
        _outbound.Publish(new ShipmentScheduled(reservationConfirmed.OrderId, reservationConfirmed.Sku));
    }
}
