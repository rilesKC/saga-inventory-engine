using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Tests;

public class InventoryParticipantTests
{
    private static async Task<InMemoryInventoryEventStore> SeedAsync(string sku, int quantity)
    {
        var eventStore = new InMemoryInventoryEventStore();
        await eventStore.AppendRangeAsync(sku, 0, [new StockSeeded(sku, quantity)], CancellationToken.None);
        return eventStore;
    }

    [Fact]
    public async Task OnOrderPlaced_WithSufficientStock_PublishesStockReserved()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        _ = new InventoryParticipant(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);
        StockReserved? published = null;
        bus.Subscribe<StockReserved>(e => published = e);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal(4, published.Quantity);
        Assert.Equal(199.99m, published.Amount);
        var item = InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal(6, item.AvailableQuantity);
    }

    [Fact]
    public async Task OnOrderPlaced_WithInsufficientStock_PublishesStockReservationFailed()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        _ = new InventoryParticipant(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);
        StockReservationFailed? published = null;
        bus.Subscribe<StockReservationFailed>(e => published = e);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 11, 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        var item = InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public async Task OnPaymentCharged_ConfirmsReservationAndPublishesReservationConfirmed()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        await eventStore.AppendRangeAsync("SKU-1", 1, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);
        _ = new InventoryParticipant(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);
        ReservationConfirmed? published = null;
        bus.Subscribe<ReservationConfirmed>(e => published = e);

        bus.Publish(new PaymentCharged("ORDER-1", "SKU-1", 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        var item = InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal(4, item.DeductedQuantity);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public async Task OnPaymentDeclined_ReleasesReservationAndPublishesReservationReleased()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        await eventStore.AppendRangeAsync("SKU-1", 1, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);
        _ = new InventoryParticipant(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);
        ReservationReleased? published = null;
        bus.Subscribe<ReservationReleased>(e => published = e);

        bus.Publish(new PaymentDeclined("ORDER-1", "SKU-1", 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        var item = InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public async Task OnOrderPlaced_SuccessfulReservation_AppendsStockReservedToEventStore()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        _ = new InventoryParticipant(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        var events = await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None);
        var stockReserved = Assert.Single(events.OfType<StockReserved>());
        Assert.Equal("ORDER-1", stockReserved.OrderId);
        Assert.Equal(4, stockReserved.Quantity);
    }

    [Fact]
    public async Task OnOrderPlaced_InsufficientStock_AppendsNothing()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        _ = new InventoryParticipant(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 11, 199.99m));

        var events = await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None);
        Assert.Empty(events.OfType<StockReserved>());
    }

    [Fact]
    public async Task OnOrderPlaced_ConcurrentWriterAppendsFirst_RetriesAgainstFreshState()
    {
        // Simulates a second task instance's write landing between this participant's read and
        // write -- the exact race that broke desired_count >= 2 before ApplyWithRetry existed.
        var bus = new EventBus();
        var eventStore = new RaceInjectingEventStore(await SeedAsync("SKU-1", 10));
        _ = new InventoryParticipant(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        var item = InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal(1, eventStore.ConflictsInjected);
        Assert.Equal(3, item.AvailableQuantity);
    }

    [Fact]
    public async Task OnOrderPlaced_SustainedConflict_RetriesUpToCapThenThrows()
    {
        // Under sustained contention (a conflict on every single attempt, not just one), the retry
        // loop must give up after a bounded number of attempts instead of spinning forever.
        var bus = new EventBus();
        var eventStore = new AlwaysConflictingEventStore(await SeedAsync("SKU-1", 10));
        _ = new InventoryParticipant(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);

        Assert.Throws<ConcurrencyConflictException>(() =>
            bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m)));

        Assert.Equal(RetryBackoff.MaxAttempts, eventStore.AttemptsMade);
    }

    /// <summary>
    /// Wraps a real event store; the first AppendRangeAsync call for a given SKU is intercepted to
    /// simulate another writer sneaking in first (appending an unrelated reservation directly, then
    /// throwing ConcurrencyConflictException), forcing the caller's retry loop to actually exercise
    /// a reload-and-reapply cycle.
    /// </summary>
    private sealed class RaceInjectingEventStore(InMemoryInventoryEventStore inner) : IInventoryEventStore
    {
        private bool _raceInjected;

        public int ConflictsInjected { get; private set; }

        public async Task AppendRangeAsync(string sku, int expectedEventCount, IReadOnlyList<object> events, CancellationToken cancellationToken)
        {
            if (!_raceInjected)
            {
                _raceInjected = true;
                ConflictsInjected++;
                await inner.AppendRangeAsync(sku, expectedEventCount, [new StockReserved(sku, "ORDER-RACE", 3, 49.99m)], cancellationToken);
                throw new ConcurrencyConflictException(sku, expectedEventCount, expectedEventCount + 1);
            }

            await inner.AppendRangeAsync(sku, expectedEventCount, events, cancellationToken);
        }

        public Task<IReadOnlyList<object>> LoadEventsAsync(string sku, CancellationToken cancellationToken) =>
            inner.LoadEventsAsync(sku, cancellationToken);
    }

    /// <summary>
    /// Simulates sustained contention: every single AppendRangeAsync call throws
    /// ConcurrencyConflictException, never succeeding, to prove the retry loop is actually bounded.
    /// </summary>
    private sealed class AlwaysConflictingEventStore(InMemoryInventoryEventStore inner) : IInventoryEventStore
    {
        public int AttemptsMade { get; private set; }

        public Task AppendRangeAsync(string sku, int expectedEventCount, IReadOnlyList<object> events, CancellationToken cancellationToken)
        {
            AttemptsMade++;
            throw new ConcurrencyConflictException(sku, expectedEventCount, expectedEventCount + 1);
        }

        public Task<IReadOnlyList<object>> LoadEventsAsync(string sku, CancellationToken cancellationToken) =>
            inner.LoadEventsAsync(sku, cancellationToken);
    }
}
