namespace Inventory.Domain;

public sealed class InventoryProjection
{
    private readonly Dictionary<string, int> _totalBySku = [];
    private readonly Dictionary<string, int> _reservedBySku = [];
    private readonly Dictionary<string, int> _deductedBySku = [];

    public int GetAvailableQuantity(string sku)
    {
        var total = _totalBySku.GetValueOrDefault(sku);
        var reserved = _reservedBySku.GetValueOrDefault(sku);
        var deducted = _deductedBySku.GetValueOrDefault(sku);
        return total - reserved - deducted;
    }

    public void Apply(object @event)
    {
        switch (@event)
        {
            case StockSeeded stockSeeded:
                _totalBySku[stockSeeded.Sku] = _totalBySku.GetValueOrDefault(stockSeeded.Sku) + stockSeeded.InitialQuantity;
                break;
            case StockReserved stockReserved:
                _reservedBySku[stockReserved.Sku] = _reservedBySku.GetValueOrDefault(stockReserved.Sku) + stockReserved.Quantity;
                break;
            case ReservationConfirmed reservationConfirmed:
                _reservedBySku[reservationConfirmed.Sku] = _reservedBySku.GetValueOrDefault(reservationConfirmed.Sku) - reservationConfirmed.Quantity;
                _deductedBySku[reservationConfirmed.Sku] = _deductedBySku.GetValueOrDefault(reservationConfirmed.Sku) + reservationConfirmed.Quantity;
                break;
            case ReservationReleased reservationReleased:
                _reservedBySku[reservationReleased.Sku] = _reservedBySku.GetValueOrDefault(reservationReleased.Sku) - reservationReleased.Quantity;
                break;
            default:
                throw new InvalidOperationException($"Unknown event type: {@event.GetType()}");
        }
    }
}
