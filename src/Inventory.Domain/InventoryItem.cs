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

    public static InventoryItem LoadFromHistory(IEnumerable<object> history)
    {
        var item = new InventoryItem();
        foreach (var @event in history)
        {
            item.ApplyHistoryEvent(@event);
        }

        return item;
    }

    private void ApplyHistoryEvent(object @event)
    {
        switch (@event)
        {
            case StockSeeded stockSeeded:
                Apply(stockSeeded);
                break;
            case StockReserved stockReserved:
                Apply(stockReserved);
                break;
            case ReservationConfirmed reservationConfirmed:
                Apply(reservationConfirmed);
                break;
            case ReservationReleased reservationReleased:
                Apply(reservationReleased);
                break;
            default:
                throw new InvalidOperationException($"Unknown event type: {@event.GetType()}");
        }
    }

    public void Handle(ReserveStock command)
    {
        if (command.Quantity <= 0)
        {
            throw new InvalidReservationRequestException(command.Sku, command.OrderId, $"quantity must be positive, was {command.Quantity}.");
        }

        if (command.Amount < 0)
        {
            throw new InvalidReservationRequestException(command.Sku, command.OrderId, $"amount cannot be negative, was {command.Amount}.");
        }

        if (_reservations.TryGetValue(command.OrderId, out var existing))
        {
            if (existing.State == ReservationState.Reserved)
            {
                // Genuine duplicate of an already-active reservation (e.g. a redelivery before this
                // event was ever published downstream) -- same outcome either way, safe to no-op.
                return;
            }

            // The reservation for this OrderId already reached a terminal state (Confirmed or
            // Released) -- this order is done, one way or another. A fresh ReserveStock for it is
            // not a duplicate of the original success, it's an attempt to reopen something final.
            // Previously this fell through the same ContainsKey check above and no-op'd silently
            // regardless of which terminal state it was in, indistinguishable from success to the
            // caller. Now explicit and loud, matching every other invalid-transition case below.
            throw new InvalidReservationStateException(command.Sku, command.OrderId, nameof(ReserveStock));
        }

        if (command.Quantity > AvailableQuantity)
        {
            throw new InsufficientStockException(command.Sku, command.Quantity, AvailableQuantity);
        }

        var stockReserved = new StockReserved(command.Sku, command.OrderId, command.Quantity, command.Amount);
        Apply(stockReserved);
        _uncommittedEvents.Add(stockReserved);
    }

    public void Handle(ConfirmReservation command)
    {
        // TryGetValue, not the indexer: an OrderId with no reservation at all (never seen a
        // ReserveStock for it) used to throw a raw KeyNotFoundException here -- an
        // implementation-detail exception type instead of the same domain exception every other
        // invalid transition in this class already uses.
        if (!_reservations.TryGetValue(command.OrderId, out var reservation) || reservation.State != ReservationState.Reserved)
        {
            throw new InvalidReservationStateException(command.Sku, command.OrderId, nameof(ConfirmReservation));
        }

        var reservationConfirmed = new ReservationConfirmed(command.Sku, command.OrderId, reservation.Quantity);
        Apply(reservationConfirmed);
        _uncommittedEvents.Add(reservationConfirmed);
    }

    public void Handle(ReleaseReservation command)
    {
        if (!_reservations.TryGetValue(command.OrderId, out var reservation) || reservation.State != ReservationState.Reserved)
        {
            throw new InvalidReservationStateException(command.Sku, command.OrderId, nameof(ReleaseReservation));
        }

        var reservationReleased = new ReservationReleased(command.Sku, command.OrderId, reservation.Quantity);
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
