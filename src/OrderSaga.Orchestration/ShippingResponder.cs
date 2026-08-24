using OrderSaga.Shared;

namespace OrderSaga.Orchestration;

public sealed class ShippingResponder
{
    private readonly EventBus _bus;

    public ShippingResponder(EventBus bus)
    {
        _bus = bus;
        _bus.Subscribe<ScheduleShipmentCommand>(OnScheduleShipmentCommand);
    }

    private void OnScheduleShipmentCommand(ScheduleShipmentCommand command)
    {
        _bus.Publish(new ShipmentScheduledReply(command.OrderId, command.Sku));
    }
}
