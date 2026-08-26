namespace Inventory.Domain.Tests;

public class InventoryProjectionTests
{
    [Fact]
    public void Apply_SeededThenReserved_ReflectsReducedAvailableQuantity()
    {
        var projection = new InventoryProjection();

        projection.Apply(new StockSeeded("SKU-1", 10));
        projection.Apply(new StockReserved("SKU-1", "ORDER-1", 4, 199.99m));

        Assert.Equal(6, projection.GetAvailableQuantity("SKU-1"));
    }

    [Fact]
    public void Apply_ReservationReleased_RestoresAvailableQuantity()
    {
        var projection = new InventoryProjection();
        projection.Apply(new StockSeeded("SKU-1", 10));
        projection.Apply(new StockReserved("SKU-1", "ORDER-1", 4, 199.99m));

        projection.Apply(new ReservationReleased("SKU-1", "ORDER-1", 4));

        Assert.Equal(10, projection.GetAvailableQuantity("SKU-1"));
    }

    [Fact]
    public void Apply_ReservationConfirmed_LeavesAvailableQuantityUnchanged()
    {
        var projection = new InventoryProjection();
        projection.Apply(new StockSeeded("SKU-1", 10));
        projection.Apply(new StockReserved("SKU-1", "ORDER-1", 4, 199.99m));

        projection.Apply(new ReservationConfirmed("SKU-1", "ORDER-1", 4));

        Assert.Equal(6, projection.GetAvailableQuantity("SKU-1"));
    }
}
