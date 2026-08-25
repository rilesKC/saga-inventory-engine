using OrderSaga.Orchestration;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Messaging;

/// <summary>
/// Subscribes to every known message type on a given outbound <see cref="EventBus"/> and forwards
/// each one through whichever <see cref="IMessagePublisher"/> the caller's selector picks for that
/// type -- a single-destination Host (Inventory, Responder) passes a selector that always returns
/// the same publisher; the Coordinator passes one that routes by command type. Explicit per-type
/// subscriptions, same precedent as choreography's OutboundEventForwarder, rather than an "any
/// message" hook added to the already-tested, shared EventBus itself.
/// </summary>
public sealed class OutboundMessageForwarder
{
    public OutboundMessageForwarder(EventBus bus, Func<Type, IMessagePublisher> publisherSelector)
    {
        void Forward<T>(T message) where T : notnull => publisherSelector(typeof(T)).Publish(message);

        bus.Subscribe<OrderPlaced>(Forward);
        bus.Subscribe<ReserveStockCommand>(Forward);
        bus.Subscribe<ConfirmReservationCommand>(Forward);
        bus.Subscribe<ReleaseReservationCommand>(Forward);
        bus.Subscribe<ChargePaymentCommand>(Forward);
        bus.Subscribe<ScheduleShipmentCommand>(Forward);
        bus.Subscribe<StockReservedReply>(Forward);
        bus.Subscribe<StockReservationFailedReply>(Forward);
        bus.Subscribe<ReservationConfirmedReply>(Forward);
        bus.Subscribe<ReservationReleasedReply>(Forward);
        bus.Subscribe<PaymentChargedReply>(Forward);
        bus.Subscribe<PaymentDeclinedReply>(Forward);
        bus.Subscribe<ShipmentScheduledReply>(Forward);
    }
}
