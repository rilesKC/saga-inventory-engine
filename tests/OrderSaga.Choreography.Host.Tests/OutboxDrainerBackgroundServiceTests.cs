using Inventory.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace OrderSaga.Choreography.Host.Tests;

public class OutboxDrainerBackgroundServiceTests
{
    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public readonly List<object> Published = [];

        public void Publish(object @event) => Published.Add(@event);
    }

    private sealed class SelectivelyThrowingEventPublisher : IEventPublisher
    {
        public readonly List<object> Published = [];

        public void Publish(object @event)
        {
            if (@event is StockReserved { OrderId: "ORDER-1" })
            {
                throw new InvalidOperationException("simulated transport failure");
            }

            Published.Add(@event);
        }
    }

    [Fact]
    public async Task DrainOnceAsync_PendingOutboundEvent_PublishesItAndMarksItPublished()
    {
        var eventStore = new InMemoryInventoryEventStore();
        await eventStore.AppendRangeAsync("SKU-1", 0, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);
        var publisher = new RecordingEventPublisher();
        var drainer = new OutboxDrainerBackgroundService(eventStore, publisher, NullLogger<OutboxDrainerBackgroundService>.Instance);

        await drainer.DrainOnceAsync(CancellationToken.None);

        var published = Assert.IsType<StockReserved>(Assert.Single(publisher.Published));
        Assert.Equal("ORDER-1", published.OrderId);
        Assert.Empty(await eventStore.LoadUnpublishedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DrainOnceAsync_CalledTwice_SecondCallPublishesNothingNew()
    {
        var eventStore = new InMemoryInventoryEventStore();
        await eventStore.AppendRangeAsync("SKU-1", 0, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);
        var publisher = new RecordingEventPublisher();
        var drainer = new OutboxDrainerBackgroundService(eventStore, publisher, NullLogger<OutboxDrainerBackgroundService>.Instance);
        await drainer.DrainOnceAsync(CancellationToken.None);

        await drainer.DrainOnceAsync(CancellationToken.None);

        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task DrainOnceAsync_PendingStockSeeded_MarksPublishedWithoutSendingToPublisher()
    {
        // StockSeeded doesn't implement IOutboundEvent -- it was never meant to leave the process
        // (see the marker interface's own doc comment). The drainer must still mark it published
        // so it stops showing up in every future poll, but never hand it to the publisher.
        var eventStore = new InMemoryInventoryEventStore();
        await eventStore.AppendRangeAsync("SKU-1", 0, [new StockSeeded("SKU-1", 100)], CancellationToken.None);
        var publisher = new RecordingEventPublisher();
        var drainer = new OutboxDrainerBackgroundService(eventStore, publisher, NullLogger<OutboxDrainerBackgroundService>.Instance);

        await drainer.DrainOnceAsync(CancellationToken.None);

        Assert.Empty(publisher.Published);
        Assert.Empty(await eventStore.LoadUnpublishedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DrainOnceAsync_OneEntryPublisherThrows_OtherEntriesInBatchStillDrainAndFailedEntryStaysPending()
    {
        var eventStore = new InMemoryInventoryEventStore();
        await eventStore.AppendRangeAsync("SKU-1", 0, [new StockReserved("SKU-1", "ORDER-1", 4, 199.99m)], CancellationToken.None);
        await eventStore.AppendRangeAsync("SKU-2", 0, [new StockReserved("SKU-2", "ORDER-2", 2, 49.99m)], CancellationToken.None);
        var publisher = new SelectivelyThrowingEventPublisher();
        var drainer = new OutboxDrainerBackgroundService(eventStore, publisher, NullLogger<OutboxDrainerBackgroundService>.Instance);

        await drainer.DrainOnceAsync(CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal("ORDER-2", Assert.IsType<StockReserved>(published).OrderId);
        var stillPending = Assert.Single(await eventStore.LoadUnpublishedAsync(CancellationToken.None));
        Assert.Equal("ORDER-1", Assert.IsType<StockReserved>(stillPending.Event).OrderId);
    }
}
