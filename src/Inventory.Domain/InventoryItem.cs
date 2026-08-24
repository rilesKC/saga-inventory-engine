namespace Inventory.Domain;

public sealed class InventoryItem
{
    private enum ReservationState { Reserved, Confirmed, Released }

    private sealed record ReservationRecord(int Quantity, ReservationState State);

    private readonly List<object> _uncommittedEvents = [];
    private readonly Dictionary<string, ReservationRecord> _reservations = [];

    public string Sku { get; private set; } = string.Empty;
    public int TotalQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public int DeductedQuantity { get; private set; }
    public int AvailableQuantity => TotalQuantity - ReservedQuantity - DeductedQuantity;
    public int Version { get; private set; }

    public IReadOnlyList<object> UncommittedEvents => _uncommittedEvents;

    private InventoryItem()
    {
    }

    public static InventoryItem Seed(string sku, int initialQuantity)
    {
        var item = new InventoryItem();
        var stockSeeded = new StockSeeded(sku, initialQuantity);
        item.Apply(stockSeeded);
        item._uncommittedEvents.Add(stockSeeded);
        return item;
    }

    public void Handle(ReserveStock command)
    {
        if (_reservations.ContainsKey(command.OrderId))
        {
            return;
        }

        if (command.Quantity > AvailableQuantity)
        {
            throw new InsufficientStockException(command.Sku, command.Quantity, AvailableQuantity);
        }

        var stockReserved = new StockReserved(command.Sku, command.OrderId, command.Quantity);
        Apply(stockReserved);
        _uncommittedEvents.Add(stockReserved);
    }

    public void Handle(ConfirmReservation command)
    {
        var reservationConfirmed = new ReservationConfirmed(command.Sku, command.OrderId);
        Apply(reservationConfirmed);
        _uncommittedEvents.Add(reservationConfirmed);
    }

    public void Handle(ReleaseReservation command)
    {
        var reservationReleased = new ReservationReleased(command.Sku, command.OrderId);
        Apply(reservationReleased);
        _uncommittedEvents.Add(reservationReleased);
    }

    private void Apply(StockSeeded stockSeeded)
    {
        Sku = stockSeeded.Sku;
        TotalQuantity = stockSeeded.InitialQuantity;
        Version++;
    }

    private void Apply(StockReserved stockReserved)
    {
        _reservations[stockReserved.OrderId] = new ReservationRecord(stockReserved.Quantity, ReservationState.Reserved);
        ReservedQuantity += stockReserved.Quantity;
        Version++;
    }

    private void Apply(ReservationConfirmed reservationConfirmed)
    {
        var reservation = _reservations[reservationConfirmed.OrderId];
        _reservations[reservationConfirmed.OrderId] = reservation with { State = ReservationState.Confirmed };
        ReservedQuantity -= reservation.Quantity;
        DeductedQuantity += reservation.Quantity;
        Version++;
    }

    private void Apply(ReservationReleased reservationReleased)
    {
        var reservation = _reservations[reservationReleased.OrderId];
        _reservations[reservationReleased.OrderId] = reservation with { State = ReservationState.Released };
        ReservedQuantity -= reservation.Quantity;
        Version++;
    }
}
