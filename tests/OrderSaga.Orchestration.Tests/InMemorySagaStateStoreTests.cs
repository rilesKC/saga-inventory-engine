namespace OrderSaga.Orchestration.Tests;

public class InMemorySagaStateStoreTests
{
    [Fact]
    public async Task SaveAsync_NewOrder_ExpectedVersionZero_Succeeds()
    {
        var store = new InMemorySagaStateStore();
        var state = new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.ReservingStock, Version: 1);

        await store.SaveAsync(state, expectedVersion: 0, CancellationToken.None);

        var all = await store.LoadAllAsync(CancellationToken.None);
        Assert.Equal([state], all);
    }

    [Fact]
    public async Task SaveAsync_ExpectedVersionMatchesActual_Succeeds()
    {
        var store = new InMemorySagaStateStore();
        var initial = new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.ReservingStock, Version: 1);
        await store.SaveAsync(initial, expectedVersion: 0, CancellationToken.None);
        var updated = initial with { Step = SagaStep.AwaitingPayment, Version = 2 };

        await store.SaveAsync(updated, expectedVersion: 1, CancellationToken.None);

        var all = await store.LoadAllAsync(CancellationToken.None);
        Assert.Equal([updated], all);
    }

    [Fact]
    public async Task SaveAsync_ExpectedVersionStale_ThrowsConcurrencyConflict()
    {
        var store = new InMemorySagaStateStore();
        var initial = new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.ReservingStock, Version: 1);
        await store.SaveAsync(initial, expectedVersion: 0, CancellationToken.None);
        // A concurrent writer already advanced this saga to Version 2 that this caller never saw.
        await store.SaveAsync(initial with { Step = SagaStep.AwaitingPayment, Version = 2 }, expectedVersion: 1, CancellationToken.None);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            store.SaveAsync(initial with { Step = SagaStep.Failed, Version = 2 }, expectedVersion: 1, CancellationToken.None));
    }

    [Fact]
    public async Task TryLoadAsync_ExistingOrder_ReturnsLatestState()
    {
        var store = new InMemorySagaStateStore();
        var state = new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.ReservingStock, Version: 1);
        await store.SaveAsync(state, expectedVersion: 0, CancellationToken.None);

        var loaded = await store.TryLoadAsync("ORDER-1", CancellationToken.None);

        Assert.Equal(state, loaded);
    }

    [Fact]
    public async Task TryLoadAsync_UnknownOrder_ReturnsNull()
    {
        var store = new InMemorySagaStateStore();

        var loaded = await store.TryLoadAsync("ORDER-UNKNOWN", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadAllAsync_NothingSaved_ReturnsEmpty()
    {
        var store = new InMemorySagaStateStore();

        var all = await store.LoadAllAsync(CancellationToken.None);

        Assert.Empty(all);
    }
}
