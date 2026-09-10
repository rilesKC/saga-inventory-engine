using Inventory.Domain;

namespace OrderSaga.Choreography.Host.Tests;

public class OrderStatusProjectionTests
{
    [Fact]
    public void Project_NoEvents_ReturnsNull()
    {
        var status = OrderStatusProjection.Project([]);

        Assert.Null(status);
    }

    [Fact]
    public void Project_OnlyStockReserved_ReturnsReserved()
    {
        var events = new object[] { new StockReserved("SKU-1", "ORDER-1", 4, 199.99m) };

        var status = OrderStatusProjection.Project(events);

        Assert.Equal("Reserved", status);
    }

    [Fact]
    public void Project_ReservedThenConfirmed_ReturnsConfirmed()
    {
        var events = new object[]
        {
            new StockReserved("SKU-1", "ORDER-1", 4, 199.99m),
            new ReservationConfirmed("SKU-1", "ORDER-1", 4),
        };

        var status = OrderStatusProjection.Project(events);

        Assert.Equal("Confirmed", status);
    }

    [Fact]
    public void Project_ReservedThenReleased_ReturnsReleased()
    {
        var events = new object[]
        {
            new StockReserved("SKU-1", "ORDER-1", 4, 199.99m),
            new ReservationReleased("SKU-1", "ORDER-1", 4),
        };

        var status = OrderStatusProjection.Project(events);

        Assert.Equal("Released", status);
    }
}
