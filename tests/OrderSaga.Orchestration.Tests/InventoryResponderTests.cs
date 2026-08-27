using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Tests;

public class InventoryResponderTests
{
    private static async Task<InMemoryInventoryEventStore> SeedAsync(string sku, int quantity)
    {
        var eventStore = new InMemoryInventoryEventStore();
        await eventStore.AppendRangeAsync(sku, 0, [new StockSeeded(sku, quantity)], CancellationToken.None);
        return eventStore;
    }

    [Fact]
    public async Task OnReserveStockCommand_WithSufficientStock_PublishesStockReservedReply()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        _ = new InventoryResponder(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);
        StockReservedReply? published = null;
        bus.Subscribe<StockReservedReply>(e => published = e);

        bus.Publish(new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        var item = InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal(6, item.AvailableQuantity);
    }

    [Fact]
    public async Task OnReserveStockCommand_WithInsufficientStock_PublishesStockReservationFailedReply()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        _ = new InventoryResponder(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);
        StockReservationFailedReply? published = null;
        bus.Subscribe<StockReservationFailedReply>(e => published = e);

        bus.Publish(new ReserveStockCommand("ORDER-1", "SKU-1", 11, 199.99m));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        var item = InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public async Task OnConfirmReservationCommand_ConfirmsReservationAndPublishesReservationConfirmedReply()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        await eventStore.AppendRangeAsync("SKU-1", 1, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);
        _ = new InventoryResponder(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);
        ReservationConfirmedReply? published = null;
        bus.Subscribe<ReservationConfirmedReply>(e => published = e);

        bus.Publish(new ConfirmReservationCommand("ORDER-1", "SKU-1"));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        var item = InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal(4, item.DeductedQuantity);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public async Task OnReleaseReservationCommand_ReleasesReservationAndPublishesReservationReleasedReply()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        await eventStore.AppendRangeAsync("SKU-1", 1, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);
        _ = new InventoryResponder(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);
        ReservationReleasedReply? published = null;
        bus.Subscribe<ReservationReleasedReply>(e => published = e);

        bus.Publish(new ReleaseReservationCommand("ORDER-1", "SKU-1"));

        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
        var item = InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public async Task OnConfirmReservationCommand_RedeliveredAfterAlreadyConfirmed_StillPublishesReservationConfirmedReply()
    {
        // Unlike choreography (fire-and-forget events), orchestration's command/reply model
        // requires a reply for every command -- SagaCoordinator is waiting on one. A redelivered
        // ConfirmReservationCommand for an order already Confirmed must still get its
        // ReservationConfirmedReply, not a silent no-op (which would stall the saga) or an uncaught
        // InvalidReservationStateException (which would crash message processing).
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        await eventStore.AppendRangeAsync("SKU-1", 1, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);
        await eventStore.AppendRangeAsync("SKU-1", 2, [new ReservationConfirmed("SKU-1", "ORDER-1", 4)], CancellationToken.None);
        _ = new InventoryResponder(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);
        ReservationConfirmedReply? published = null;
        bus.Subscribe<ReservationConfirmedReply>(e => published = e);

        var exception = Record.Exception(() => bus.Publish(new ConfirmReservationCommand("ORDER-1", "SKU-1")));

        Assert.Null(exception);
        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
    }

    [Fact]
    public async Task OnReleaseReservationCommand_RedeliveredAfterAlreadyReleased_StillPublishesReservationReleasedReply()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        await eventStore.AppendRangeAsync("SKU-1", 1, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);
        await eventStore.AppendRangeAsync("SKU-1", 2, [new ReservationReleased("SKU-1", "ORDER-1", 4)], CancellationToken.None);
        _ = new InventoryResponder(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);
        ReservationReleasedReply? published = null;
        bus.Subscribe<ReservationReleasedReply>(e => published = e);

        var exception = Record.Exception(() => bus.Publish(new ReleaseReservationCommand("ORDER-1", "SKU-1")));

        Assert.Null(exception);
        Assert.NotNull(published);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal("ORDER-1", published.OrderId);
    }

    [Fact]
    public async Task OnReserveStockCommand_SuccessfulReservation_AppendsStockReservedToEventStore()
    {
        var bus = new EventBus();
        var eventStore = await SeedAsync("SKU-1", 10);
        _ = new InventoryResponder(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);

        bus.Publish(new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m));

        var events = await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None);
        var stockReserved = Assert.Single(events.OfType<StockReserved>());
        Assert.Equal("ORDER-1", stockReserved.OrderId);
        Assert.Equal(4, stockReserved.Quantity);
        Assert.Equal(199.99m, stockReserved.Amount);
    }

    [Fact]
    public async Task OnReserveStockCommand_ConcurrentWriterAppendsFirst_RetriesAgainstFreshState()
    {
        // Simulates a second task instance's write landing between this responder's read and
        // write -- the exact race that broke desired_count >= 2 before ApplyWithRetry existed.
        var bus = new EventBus();
        var eventStore = new RaceInjectingEventStore(await SeedAsync("SKU-1", 10));
        _ = new InventoryResponder(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);

        bus.Publish(new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m));

        var item = InventoryItem.LoadFromHistory(await eventStore.LoadEventsAsync("SKU-1", CancellationToken.None));
        Assert.Equal(1, eventStore.ConflictsInjected);
        Assert.Equal(3, item.AvailableQuantity);
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
                // Fully qualified: this test file's namespace (OrderSaga.Orchestration.Tests) nests
                // under OrderSaga.Orchestration, which declares its own ConcurrencyConflictException --
                // an unqualified reference here resolves to that one instead of Inventory.Domain's,
                // silently testing the wrong type. That exact collision is what let InventoryResponder's
                // real catch clause bind to the wrong exception undetected (see InventoryResponder.cs).
                throw new Inventory.Domain.ConcurrencyConflictException(sku, expectedEventCount, expectedEventCount + 1);
            }

            await inner.AppendRangeAsync(sku, expectedEventCount, events, cancellationToken);
        }

        public Task<IReadOnlyList<object>> LoadEventsAsync(string sku, CancellationToken cancellationToken) =>
            inner.LoadEventsAsync(sku, cancellationToken);
    }

    [Fact]
    public async Task OnReserveStockCommand_SustainedConflict_RetriesUpToCapThenThrows()
    {
        // Under sustained contention (a conflict on every single attempt, not just one), the retry
        // loop must give up after a bounded number of attempts instead of spinning forever.
        var bus = new EventBus();
        var eventStore = new AlwaysConflictingEventStore(await SeedAsync("SKU-1", 10));
        _ = new InventoryResponder(new InboundEventBus(bus), new OutboundEventBus(bus), eventStore);

        Assert.Throws<Inventory.Domain.ConcurrencyConflictException>(() =>
            bus.Publish(new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m)));

        Assert.Equal(RetryBackoff.MaxAttempts, eventStore.AttemptsMade);
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
            throw new Inventory.Domain.ConcurrencyConflictException(sku, expectedEventCount, expectedEventCount + 1);
        }

        public Task<IReadOnlyList<object>> LoadEventsAsync(string sku, CancellationToken cancellationToken) =>
            inner.LoadEventsAsync(sku, cancellationToken);
    }
}
