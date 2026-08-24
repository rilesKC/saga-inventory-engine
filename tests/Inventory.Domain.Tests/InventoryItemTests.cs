namespace Inventory.Domain.Tests;

public class InventoryItemTests
{
    [Fact]
    public void Seed_SetsAvailableQuantityToInitialQuantity()
    {
        var item = InventoryItem.Seed("SKU-1", 10);

        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public void Seed_EmitsStockSeededEvent()
    {
        var item = InventoryItem.Seed("SKU-1", 10);

        var @event = Assert.Single(item.UncommittedEvents);
        var stockSeeded = Assert.IsType<StockSeeded>(@event);
        Assert.Equal("SKU-1", stockSeeded.Sku);
        Assert.Equal(10, stockSeeded.InitialQuantity);
    }

    [Fact]
    public void ReserveStock_WithSufficientQuantity_EmitsStockReservedAndReducesAvailableQuantity()
    {
        var item = InventoryItem.Seed("SKU-1", 10);

        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));

        Assert.Equal(6, item.AvailableQuantity);
        var stockReserved = Assert.IsType<StockReserved>(item.UncommittedEvents[^1]);
        Assert.Equal("SKU-1", stockReserved.Sku);
        Assert.Equal("ORDER-1", stockReserved.OrderId);
        Assert.Equal(4, stockReserved.Quantity);
    }

    [Fact]
    public void ReserveStock_ExceedingAvailableQuantity_ThrowsInsufficientStockExceptionAndEmitsNoEvent()
    {
        var item = InventoryItem.Seed("SKU-1", 10);

        Assert.Throws<InsufficientStockException>(() =>
            item.Handle(new ReserveStock("SKU-1", "ORDER-1", 11)));

        Assert.Equal(10, item.AvailableQuantity);
        Assert.Single(item.UncommittedEvents);
    }

    [Fact]
    public void ReserveStock_DuplicateForSameOrderAndSku_ReturnsExistingReservationWithoutReducingAvailableAgain()
    {
        var item = InventoryItem.Seed("SKU-1", 10);
        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));

        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));

        Assert.Equal(6, item.AvailableQuantity);
        Assert.Equal(2, item.UncommittedEvents.Count);
    }

    [Fact]
    public void ConfirmReservation_OnReservedHold_EmitsReservationConfirmedAndMovesQuantityToDeducted()
    {
        var item = InventoryItem.Seed("SKU-1", 10);
        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));

        item.Handle(new ConfirmReservation("SKU-1", "ORDER-1"));

        Assert.Equal(6, item.AvailableQuantity);
        Assert.Equal(4, item.DeductedQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        var confirmed = Assert.IsType<ReservationConfirmed>(item.UncommittedEvents[^1]);
        Assert.Equal("SKU-1", confirmed.Sku);
        Assert.Equal("ORDER-1", confirmed.OrderId);
    }

    [Fact]
    public void ReleaseReservation_OnReservedHold_EmitsReservationReleasedAndRestoresAvailableQuantity()
    {
        var item = InventoryItem.Seed("SKU-1", 10);
        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));

        item.Handle(new ReleaseReservation("SKU-1", "ORDER-1"));

        Assert.Equal(10, item.AvailableQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        var released = Assert.IsType<ReservationReleased>(item.UncommittedEvents[^1]);
        Assert.Equal("SKU-1", released.Sku);
        Assert.Equal("ORDER-1", released.OrderId);
    }

    [Fact]
    public void ConfirmReservation_AlreadyReleased_ThrowsInvalidReservationStateException()
    {
        var item = InventoryItem.Seed("SKU-1", 10);
        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));
        item.Handle(new ReleaseReservation("SKU-1", "ORDER-1"));

        Assert.Throws<InvalidReservationStateException>(() =>
            item.Handle(new ConfirmReservation("SKU-1", "ORDER-1")));
    }

    [Fact]
    public void ReleaseReservation_AlreadyConfirmed_ThrowsInvalidReservationStateException()
    {
        var item = InventoryItem.Seed("SKU-1", 10);
        item.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));
        item.Handle(new ConfirmReservation("SKU-1", "ORDER-1"));

        Assert.Throws<InvalidReservationStateException>(() =>
            item.Handle(new ReleaseReservation("SKU-1", "ORDER-1")));
    }

    [Fact]
    public void LoadFromHistory_ReplayingAFullLifecycle_ReconstructsTheSameStateAsLiveMethodCalls()
    {
        var live = InventoryItem.Seed("SKU-1", 10);
        live.Handle(new ReserveStock("SKU-1", "ORDER-1", 4));
        live.Handle(new ConfirmReservation("SKU-1", "ORDER-1"));

        var replayed = InventoryItem.LoadFromHistory(live.UncommittedEvents);

        Assert.Equal(live.Sku, replayed.Sku);
        Assert.Equal(live.TotalQuantity, replayed.TotalQuantity);
        Assert.Equal(live.ReservedQuantity, replayed.ReservedQuantity);
        Assert.Equal(live.DeductedQuantity, replayed.DeductedQuantity);
        Assert.Equal(live.AvailableQuantity, replayed.AvailableQuantity);
        Assert.Equal(live.Version, replayed.Version);
    }
}
