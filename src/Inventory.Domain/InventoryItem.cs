namespace Inventory.Domain;

public sealed class InventoryItem
{
    private enum ReservationState { Reserved, Confirmed, Released }

    private readonly List<object> _uncommittedEvents = [];
    private readonly Dictionary<string, ReservationState> _reservations = [];

    public string Sku { get; private set; } = string.Empty;
    public int TotalQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public int AvailableQuantity => TotalQuantity - ReservedQuantity;
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

    private void Apply(StockSeeded stockSeeded)
    {
        Sku = stockSeeded.Sku;
        TotalQuantity = stockSeeded.InitialQuantity;
        Version++;
    }

    private void Apply(StockReserved stockReserved)
    {
        _reservations[stockReserved.OrderId] = ReservationState.Reserved;
        ReservedQuantity += stockReserved.Quantity;
        Version++;
    }
}
