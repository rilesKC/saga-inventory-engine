using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration;

public sealed class InventoryResponder
{
    private readonly InboundEventBus _inbound;
    private readonly OutboundEventBus _outbound;
    private readonly Dictionary<string, InventoryItem> _items;

    /// <param name="inbound">Subscribed to for trigger commands.</param>
    /// <param name="outbound">Published to for produced replies. See
    /// <see cref="SagaCoordinator"/>'s constructor for why these are separate.</param>
    public InventoryResponder(InboundEventBus inbound, OutboundEventBus outbound, Dictionary<string, InventoryItem> items)
    {
        _inbound = inbound;
        _outbound = outbound;
        _items = items;
        _inbound.Subscribe<ReserveStockCommand>(OnReserveStockCommand);
        _inbound.Subscribe<ConfirmReservationCommand>(OnConfirmReservationCommand);
        _inbound.Subscribe<ReleaseReservationCommand>(OnReleaseReservationCommand);
    }

    private void OnReserveStockCommand(ReserveStockCommand command)
    {
        var item = _items[command.Sku];

        try
        {
            item.Handle(new ReserveStock(command.Sku, command.OrderId, command.Quantity));
        }
        catch (InsufficientStockException)
        {
            _outbound.Publish(new StockReservationFailedReply(command.OrderId, command.Sku));
            return;
        }

        _outbound.Publish(new StockReservedReply(command.OrderId, command.Sku));
    }

    private void OnConfirmReservationCommand(ConfirmReservationCommand command)
    {
        var item = _items[command.Sku];
        item.Handle(new ConfirmReservation(command.Sku, command.OrderId));
        _outbound.Publish(new ReservationConfirmedReply(command.OrderId, command.Sku));
    }

    private void OnReleaseReservationCommand(ReleaseReservationCommand command)
    {
        var item = _items[command.Sku];
        item.Handle(new ReleaseReservation(command.Sku, command.OrderId));
        _outbound.Publish(new ReservationReleasedReply(command.OrderId, command.Sku));
    }
}
