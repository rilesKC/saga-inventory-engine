using Inventory.Domain;

namespace OrderSaga.Choreography;

public sealed class ShippingStub
{
    private readonly EventBus _bus;

    public ShippingStub(EventBus bus)
    {
        _bus = bus;
        _bus.Subscribe<ReservationConfirmed>(OnReservationConfirmed);
    }

    private void OnReservationConfirmed(ReservationConfirmed reservationConfirmed)
    {
        _bus.Publish(new ShipmentScheduled(reservationConfirmed.OrderId, reservationConfirmed.Sku));
    }
}
