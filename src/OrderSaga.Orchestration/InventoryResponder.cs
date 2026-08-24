using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration;

public sealed class InventoryResponder
{
    private readonly EventBus _bus;
    private readonly Dictionary<string, InventoryItem> _items;

    public InventoryResponder(EventBus bus, Dictionary<string, InventoryItem> items)
    {
        _bus = bus;
        _items = items;
        _bus.Subscribe<ReserveStockCommand>(OnReserveStockCommand);
        _bus.Subscribe<ConfirmReservationCommand>(OnConfirmReservationCommand);
        _bus.Subscribe<ReleaseReservationCommand>(OnReleaseReservationCommand);
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
            _bus.Publish(new StockReservationFailedReply(command.OrderId, command.Sku));
            return;
        }

        _bus.Publish(new StockReservedReply(command.OrderId, command.Sku));
    }

    private void OnConfirmReservationCommand(ConfirmReservationCommand command)
    {
        var item = _items[command.Sku];
        item.Handle(new ConfirmReservation(command.Sku, command.OrderId));
        _bus.Publish(new ReservationConfirmedReply(command.OrderId, command.Sku));
    }

    private void OnReleaseReservationCommand(ReleaseReservationCommand command)
    {
        var item = _items[command.Sku];
        item.Handle(new ReleaseReservation(command.Sku, command.OrderId));
        _bus.Publish(new ReservationReleasedReply(command.OrderId, command.Sku));
    }
}
