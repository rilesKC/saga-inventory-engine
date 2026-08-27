using System.Text.Json;
using OrderSaga.Aws;
using OrderSaga.Orchestration;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Messaging.Tests;

public class SqsMessageProcessorTests
{
    // Unlike choreography (delivered via an EventBridge rule target, where the real payload is
    // nested under "detail"), this transport is direct SQS -- the raw message body IS the
    // serialized MessageEnvelope, nothing wrapping it.
    private static string ToRawBody(MessageEnvelope envelope) => JsonSerializer.Serialize(envelope);

    [Fact]
    public async Task ProcessMessageAsync_NewEnvelope_ClaimsDispatchesAndReturnsTrue()
    {
        var bus = new EventBus();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var processor = new SqsMessageProcessor(bus, idempotencyStore);
        ReserveStockCommand? dispatched = null;
        bus.Subscribe<ReserveStockCommand>(e => dispatched = e);
        var envelope = OrchestrationMessageTypeRegistry.Serialize(new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m));

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
        bus.Subscribe<ReserveStockCommand>(_ => dispatchCount++);
        var envelope = OrchestrationMessageTypeRegistry.Serialize(new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m));
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
        var idempotencyStore = new InMemoryIdempotencyStore();
        var envelope = OrchestrationMessageTypeRegistry.Serialize(new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m));
        var rawBody = ToRawBody(envelope);
        var failingBus = new EventBus();
        failingBus.Subscribe<ReserveStockCommand>(_ => throw new InvalidOperationException("simulated downstream failure"));
        var failingProcessor = new SqsMessageProcessor(failingBus, idempotencyStore);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failingProcessor.ProcessMessageAsync(rawBody, CancellationToken.None));

        var healthyBus = new EventBus();
        ReserveStockCommand? dispatched = null;
        healthyBus.Subscribe<ReserveStockCommand>(e => dispatched = e);
        var retryProcessor = new SqsMessageProcessor(healthyBus, idempotencyStore);
        var retryResult = await retryProcessor.ProcessMessageAsync(rawBody, CancellationToken.None);

        Assert.True(retryResult);
        Assert.NotNull(dispatched);
        Assert.Equal("ORDER-1", dispatched.OrderId);
    }
}
