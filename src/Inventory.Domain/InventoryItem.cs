namespace Inventory.Domain;

public sealed class InventoryItem
{
    private readonly List<object> _uncommittedEvents = [];

    public string Sku { get; private set; } = string.Empty;
    public int TotalQuantity { get; private set; }
    public int AvailableQuantity => TotalQuantity;
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

    private void Apply(StockSeeded stockSeeded)
    {
        Sku = stockSeeded.Sku;
        TotalQuantity = stockSeeded.InitialQuantity;
        Version++;
    }
}
