using Inventory.Domain;

namespace OrderSaga.Choreography.Tests;

public class InventoryParticipantTests
{
    [Fact]
    public void OnOrderPlaced_WithSufficientStock_PublishesStockReserved()
    {
        var bus = new EventBus();
        var item = InventoryItem.Seed("SKU-1", 10);
        _ = new InventoryParticipant(bus, new Dictionary<string, InventoryItem> { ["SKU-1"] = item });
        StockReserved? published = null;
        bus.Subscribe<StockReserved>(e => published = e);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(4, published.Quantity);
        Assert.Equal(6, item.AvailableQuantity);
    }
}
