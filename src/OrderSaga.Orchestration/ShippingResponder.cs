using OrderSaga.Shared;

namespace OrderSaga.Orchestration;

public sealed class ShippingResponder
{
    private readonly InboundEventBus _inbound;
    private readonly OutboundEventBus _outbound;

    /// <param name="inbound">Subscribed to for trigger commands.</param>
    /// <param name="outbound">Published to for produced replies. See
    /// <see cref="SagaCoordinator"/>'s constructor for why these are separate.</param>
    public ShippingResponder(InboundEventBus inbound, OutboundEventBus outbound)
    {
        _inbound = inbound;
        _outbound = outbound;
        _inbound.Subscribe<ScheduleShipmentCommand>(OnScheduleShipmentCommand);
    }

    private void OnScheduleShipmentCommand(ScheduleShipmentCommand command)
    {
        _outbound.Publish(new ShipmentScheduledReply(command.OrderId, command.Sku));
    }
}
