using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Tests;

public class SagaCoordinatorTests
{
    [Fact]
    public void OnOrderPlaced_CreatesSagaStateAndPublishesReserveStockCommand()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), new InMemorySagaStateStore());
        ReserveStockCommand? published = null;
        bus.Subscribe<ReserveStockCommand>(e => published = e);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.NotNull(published);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(4, published.Quantity);
        Assert.Equal(SagaStep.ReservingStock, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public void OnOrderPlaced_SagaAlreadyExistsForOrderId_DoesNotResetOrRepublish()
    {
        // A client retrying POST /orders after a timeout (or a double-click, or a back-button
        // resubmit) mints a brand-new envelope MessageId each time -- the DynamoDB MessageId-keyed
        // idempotency store never sees a duplicate, so the coordinator itself is the only thing
        // that can catch a repeated OrderPlaced for the same OrderId. Before this fix, the second
        // OrderPlaced reset the saga (even one already Completed) back to ReservingStock and
        // re-published ReserveStockCommand, re-driving the whole saga -- including a second
        // ChargePaymentCommand for an order that may already have been paid.
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), new InMemorySagaStateStore());
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));
        bus.Publish(new StockReservedReply("ORDER-1", "SKU-1"));
        Assert.Equal(SagaStep.AwaitingPayment, coordinator.GetStep("ORDER-1"));
        var republishCount = 0;
        bus.Subscribe<ReserveStockCommand>(_ => republishCount++);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        Assert.Equal(SagaStep.AwaitingPayment, coordinator.GetStep("ORDER-1"));
        Assert.Equal(0, republishCount);
    }

    [Fact]
    public void OnStockReservedReply_NoSagaStateForOrder_ThrowsSagaNotFoundException()
    {
        // A reply arriving for an OrderId the coordinator has no persisted SagaState for (e.g. its
        // OrderPlaced write never landed) must fail loudly and diagnosably, not with an opaque
        // NullReferenceException from blindly dereferencing a null current state.
        var bus = new EventBus();
        _ = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), new InMemorySagaStateStore());

        var exception = Assert.Throws<SagaNotFoundException>(() =>
            bus.Publish(new StockReservedReply("ORDER-ORPHAN", "SKU-1")));

        Assert.Equal("ORDER-ORPHAN", exception.OrderId);
    }

    [Fact]
    public void OnStockReservedReply_PublishesChargePaymentCommandWithSagaAmount()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), new InMemorySagaStateStore());
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));
        ChargePaymentCommand? published = null;
        bus.Subscribe<ChargePaymentCommand>(e => published = e);

        bus.Publish(new StockReservedReply("ORDER-1", "SKU-1"));

        Assert.NotNull(published);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(199.99m, published.Amount);
        Assert.Equal(SagaStep.AwaitingPayment, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public void OnStockReservationFailedReply_MarksSagaFailed()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), new InMemorySagaStateStore());
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        bus.Publish(new StockReservationFailedReply("ORDER-1", "SKU-1"));

        Assert.Equal(SagaStep.Failed, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public void OnPaymentChargedReply_PublishesConfirmReservationCommand()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), new InMemorySagaStateStore());
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));
        ConfirmReservationCommand? published = null;
        bus.Subscribe<ConfirmReservationCommand>(e => published = e);

        bus.Publish(new PaymentChargedReply("ORDER-1", "SKU-1"));

        Assert.NotNull(published);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(SagaStep.Confirming, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public void OnPaymentDeclinedReply_PublishesReleaseReservationCommand()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), new InMemorySagaStateStore());
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 999.99m));
        ReleaseReservationCommand? published = null;
        bus.Subscribe<ReleaseReservationCommand>(e => published = e);

        bus.Publish(new PaymentDeclinedReply("ORDER-1", "SKU-1"));

        Assert.NotNull(published);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(SagaStep.Compensating, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public void OnReservationConfirmedReply_PublishesScheduleShipmentCommand()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), new InMemorySagaStateStore());
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));
        ScheduleShipmentCommand? published = null;
        bus.Subscribe<ScheduleShipmentCommand>(e => published = e);

        bus.Publish(new ReservationConfirmedReply("ORDER-1", "SKU-1"));

        Assert.NotNull(published);
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Equal("SKU-1", published.Sku);
        Assert.Equal(SagaStep.SchedulingShipment, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public void OnReservationReleasedReply_MarksSagaCompensated()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), new InMemorySagaStateStore());
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 999.99m));

        bus.Publish(new ReservationReleasedReply("ORDER-1", "SKU-1"));

        Assert.Equal(SagaStep.Compensated, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public void OnShipmentScheduledReply_MarksSagaCompleted()
    {
        var bus = new EventBus();
        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), new InMemorySagaStateStore());
        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        bus.Publish(new ShipmentScheduledReply("ORDER-1", "SKU-1"));

        Assert.Equal(SagaStep.Completed, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public async Task Constructor_GivenPersistedSagaState_RehydratesInMemoryState()
    {
        var bus = new EventBus();
        var store = new InMemorySagaStateStore();
        var persisted = new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.AwaitingPayment, Version: 1);
        await store.SaveAsync(persisted, 0, CancellationToken.None);

        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), store);

        Assert.Equal(SagaStep.AwaitingPayment, coordinator.GetStep("ORDER-1"));
    }

    [Fact]
    public async Task OnOrderPlaced_NewOrder_PersistsInitialState()
    {
        var bus = new EventBus();
        var store = new InMemorySagaStateStore();
        _ = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), store);

        bus.Publish(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        var all = await store.LoadAllAsync(CancellationToken.None);
        var saved = Assert.Single(all);
        Assert.Equal("ORDER-1", saved.OrderId);
        Assert.Equal(SagaStep.ReservingStock, saved.Step);
    }

    [Fact]
    public async Task OnStockReservedReply_ConcurrentWriterSavesFirst_RetriesAgainstFreshState()
    {
        // Simulates a second Coordinator instance's write landing between this coordinator's read
        // and write -- the exact race that broke desired_count >= 2 before ApplyWithRetry existed.
        var bus = new EventBus();
        var store = new InMemorySagaStateStore();
        await store.SaveAsync(new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.ReservingStock, Version: 1), 0, CancellationToken.None);
        var raceInjectingStore = new RaceInjectingSagaStateStore(store);
        var coordinator = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), raceInjectingStore);

        bus.Publish(new StockReservedReply("ORDER-1", "SKU-1"));

        Assert.Equal(1, raceInjectingStore.ConflictsInjected);
        Assert.Equal(SagaStep.AwaitingPayment, coordinator.GetStep("ORDER-1"));
    }

    /// <summary>
    /// Wraps a real saga state store; the first SaveAsync call for a given OrderId is intercepted
    /// to simulate another writer sneaking in first (saving an unrelated transition directly, then
    /// throwing ConcurrencyConflictException), forcing the caller's retry loop to actually exercise
    /// a reload-and-reapply cycle.
    /// </summary>
    private sealed class RaceInjectingSagaStateStore(InMemorySagaStateStore inner) : ISagaStateStore
    {
        private bool _raceInjected;

        public int ConflictsInjected { get; private set; }

        public async Task SaveAsync(SagaState state, int expectedVersion, CancellationToken cancellationToken)
        {
            if (!_raceInjected)
            {
                _raceInjected = true;
                ConflictsInjected++;
                await inner.SaveAsync(state with { Step = SagaStep.Failed, Version = expectedVersion + 1 }, expectedVersion, cancellationToken);
                throw new ConcurrencyConflictException(state.OrderId, expectedVersion, expectedVersion + 1);
            }

            await inner.SaveAsync(state, expectedVersion, cancellationToken);
        }

        public Task<SagaState?> TryLoadAsync(string orderId, CancellationToken cancellationToken) =>
            inner.TryLoadAsync(orderId, cancellationToken);

        public Task<IReadOnlyList<SagaState>> LoadAllAsync(CancellationToken cancellationToken) =>
            inner.LoadAllAsync(cancellationToken);
    }

    [Fact]
    public void OnStockReservedReply_SustainedConflict_RetriesUpToCapThenThrows()
    {
        // Under sustained contention (a conflict on every single attempt, not just one), the retry
        // loop must give up after a bounded number of attempts instead of spinning forever.
        var bus = new EventBus();
        var store = new AlwaysConflictingSagaStateStore();
        _ = new SagaCoordinator(new InboundEventBus(bus), new OutboundEventBus(bus), store);

        Assert.Throws<ConcurrencyConflictException>(() =>
            bus.Publish(new StockReservedReply("ORDER-1", "SKU-1")));

        Assert.Equal(RetryBackoff.MaxAttempts, store.AttemptsMade);
    }

    /// <summary>
    /// Simulates sustained contention: every single SaveAsync call throws
    /// ConcurrencyConflictException, never succeeding, to prove the retry loop is actually bounded.
    /// Always reports a persisted SagaState for OnStockReservedReply's own precondition (a saga
    /// must already exist to transition) so the test exercises the retry cap, not
    /// SagaNotFoundException.
    /// </summary>
    private sealed class AlwaysConflictingSagaStateStore : ISagaStateStore
    {
        public int AttemptsMade { get; private set; }

        public Task SaveAsync(SagaState state, int expectedVersion, CancellationToken cancellationToken)
        {
            AttemptsMade++;
            throw new ConcurrencyConflictException(state.OrderId, expectedVersion, expectedVersion + 1);
        }

        public Task<SagaState?> TryLoadAsync(string orderId, CancellationToken cancellationToken) =>
            Task.FromResult<SagaState?>(new SagaState(orderId, "SKU-1", 4, 199.99m, SagaStep.ReservingStock, Version: 1));

        public Task<IReadOnlyList<SagaState>> LoadAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SagaState>>([]);
    }
}
