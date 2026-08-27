using System.Text.Json;
using OrderSaga.Aws;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Host.Tests;

public class SqsMessageProcessorTests
{
    // Matches the real shape of an SQS message delivered by an EventBridge rule target: the full
    // EventBridge event structure, with our EventEnvelope nested under "detail" -- not the
    // envelope directly. A LocalStack run caught that this test's original shape (the envelope as
    // the whole body) didn't match reality and let a real bug through.
    private static string ToRawBody(EventEnvelope envelope) => JsonSerializer.Serialize(new
    {
        version = "0",
        id = Guid.NewGuid().ToString(),
        detail_type = envelope.EventType,
        source = "order-saga-choreography.host",
        detail = envelope,
    });

    [Fact]
    public async Task ProcessMessageAsync_NewEnvelope_ClaimsDispatchesAndReturnsTrue()
    {
        var bus = new EventBus();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var processor = new SqsMessageProcessor(bus, idempotencyStore);
        OrderPlaced? dispatched = null;
        bus.Subscribe<OrderPlaced>(e => dispatched = e);
        var envelope = EventTypeRegistry.Serialize(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        var result = await processor.ProcessMessageAsync(ToRawBody(envelope), CancellationToken.None);

        Assert.True(result, "a genuinely-processed message must return true so SqsPollingBackgroundService deletes it");
        Assert.NotNull(dispatched);
        Assert.Equal("ORDER-1", dispatched.OrderId);
    }

    [Fact]
    public async Task ProcessMessageAsync_DuplicateMessageId_SkipsDispatchAndReturnsFalse()
    {
        // false, not true, matters beyond "don't dispatch twice": SqsPollingBackgroundService only
        // deletes the SQS message when this returns true. If a redelivered/concurrent copy of a
        // message still being processed by another delivery returned true here, that copy would
        // delete the message out from under the delivery that actually holds the claim -- if that
        // delivery later fails and expects a redelivery to retry, the message would already be gone.
        var bus = new EventBus();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var processor = new SqsMessageProcessor(bus, idempotencyStore);
        var dispatchCount = 0;
        bus.Subscribe<OrderPlaced>(_ => dispatchCount++);
        var envelope = EventTypeRegistry.Serialize(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));
        var rawBody = ToRawBody(envelope);
        var firstResult = await processor.ProcessMessageAsync(rawBody, CancellationToken.None);

        var secondResult = await processor.ProcessMessageAsync(rawBody, CancellationToken.None);

        Assert.True(firstResult);
        Assert.False(secondResult);
        Assert.Equal(1, dispatchCount);
    }

    [Fact]
    public async Task ProcessMessageAsync_DispatchThrows_ReleasesClaimSoRedeliveryCanRetry()
    {
        // The claim is taken before dispatch (to stop two concurrent deliveries from both passing
        // TryClaim and double-processing), so a failure between the claim and a successful publish
        // must release the claim -- otherwise the message is left un-deleted for legitimate SQS
        // redelivery, but redelivery finds the MessageId already claimed and silently no-ops
        // instead of retrying, permanently dropping the event.
        var idempotencyStore = new InMemoryIdempotencyStore();
        var envelope = EventTypeRegistry.Serialize(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));
        var rawBody = ToRawBody(envelope);
        var failingBus = new EventBus();
        failingBus.Subscribe<OrderPlaced>(_ => throw new InvalidOperationException("simulated downstream failure"));
        var failingProcessor = new SqsMessageProcessor(failingBus, idempotencyStore);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failingProcessor.ProcessMessageAsync(rawBody, CancellationToken.None));

        var healthyBus = new EventBus();
        OrderPlaced? dispatched = null;
        healthyBus.Subscribe<OrderPlaced>(e => dispatched = e);
        var retryProcessor = new SqsMessageProcessor(healthyBus, idempotencyStore);
        var retryResult = await retryProcessor.ProcessMessageAsync(rawBody, CancellationToken.None);

        Assert.True(retryResult);
        Assert.NotNull(dispatched);
        Assert.Equal("ORDER-1", dispatched.OrderId);
    }
}
