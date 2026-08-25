using System.Text.Json;
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
    public async Task ProcessMessageAsync_NewEnvelope_ClaimsAndDispatchesToEventBus()
    {
        var bus = new EventBus();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var processor = new SqsMessageProcessor(bus, idempotencyStore);
        ReserveStockCommand? dispatched = null;
        bus.Subscribe<ReserveStockCommand>(e => dispatched = e);
        var envelope = OrchestrationMessageTypeRegistry.Serialize(new ReserveStockCommand("ORDER-1", "SKU-1", 4));

        await processor.ProcessMessageAsync(ToRawBody(envelope), CancellationToken.None);

        Assert.NotNull(dispatched);
        Assert.Equal("ORDER-1", dispatched.OrderId);
    }

    [Fact]
    public async Task ProcessMessageAsync_DuplicateMessageId_SkipsDispatch()
    {
        var bus = new EventBus();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var processor = new SqsMessageProcessor(bus, idempotencyStore);
        var dispatchCount = 0;
        bus.Subscribe<ReserveStockCommand>(_ => dispatchCount++);
        var envelope = OrchestrationMessageTypeRegistry.Serialize(new ReserveStockCommand("ORDER-1", "SKU-1", 4));
        var rawBody = ToRawBody(envelope);
        await processor.ProcessMessageAsync(rawBody, CancellationToken.None);

        await processor.ProcessMessageAsync(rawBody, CancellationToken.None);

        Assert.Equal(1, dispatchCount);
    }

    [Fact]
    public async Task ProcessMessageAsync_DispatchThrows_ReleasesClaimSoRedeliveryCanRetry()
    {
        var idempotencyStore = new InMemoryIdempotencyStore();
        var envelope = OrchestrationMessageTypeRegistry.Serialize(new ReserveStockCommand("ORDER-1", "SKU-1", 4));
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
        await retryProcessor.ProcessMessageAsync(rawBody, CancellationToken.None);

        Assert.NotNull(dispatched);
        Assert.Equal("ORDER-1", dispatched.OrderId);
    }
}
