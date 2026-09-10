namespace Inventory.Domain.Tests;

public class InventoryEventOrderIdTests
{
    [Fact]
    public void TryGet_StockReserved_ReturnsOrderId()
    {
        Assert.Equal("ORDER-1", InventoryEventOrderId.TryGet(new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)));
    }

    [Fact]
    public void TryGet_ReservationConfirmed_ReturnsOrderId()
    {
        Assert.Equal("ORDER-1", InventoryEventOrderId.TryGet(new ReservationConfirmed("SKU-1", "ORDER-1", 4)));
    }

    [Fact]
    public void TryGet_ReservationReleased_ReturnsOrderId()
    {
        Assert.Equal("ORDER-1", InventoryEventOrderId.TryGet(new ReservationReleased("SKU-1", "ORDER-1", 4)));
    }

    [Fact]
    public void TryGet_StockSeeded_ReturnsNull()
    {
        Assert.Null(InventoryEventOrderId.TryGet(new StockSeeded("SKU-1", 100)));
    }
}
