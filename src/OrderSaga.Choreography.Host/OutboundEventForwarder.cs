using Inventory.Domain;
using OrderSaga.Choreography;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Subscribes to every known event type on the local <see cref="EventBus"/> and forwards each one
/// to EventBridge via <see cref="IEventPublisher"/> -- explicit per-type subscriptions rather than
/// an "any event" hook added to EventBus itself, keeping that already-tested shared code untouched.
/// </summary>
public sealed class OutboundEventForwarder
{
    public OutboundEventForwarder(EventBus bus, IEventPublisher publisher)
    {
        bus.Subscribe<OrderPlaced>(publisher.Publish);
        bus.Subscribe<StockReserved>(publisher.Publish);
        bus.Subscribe<StockReservationFailed>(publisher.Publish);
        bus.Subscribe<PaymentCharged>(publisher.Publish);
        bus.Subscribe<PaymentDeclined>(publisher.Publish);
        bus.Subscribe<ReservationConfirmed>(publisher.Publish);
        bus.Subscribe<ReservationReleased>(publisher.Publish);
        bus.Subscribe<ShipmentScheduled>(publisher.Publish);
    }
}
