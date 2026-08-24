using System.Text.Json;
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
    public void ProcessMessage_NewEnvelope_ClaimsAndDispatchesToEventBus()
    {
        var bus = new EventBus();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var processor = new SqsMessageProcessor(bus, idempotencyStore);
        OrderPlaced? dispatched = null;
        bus.Subscribe<OrderPlaced>(e => dispatched = e);
        var envelope = EventTypeRegistry.Serialize(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));

        processor.ProcessMessage(ToRawBody(envelope));

        Assert.NotNull(dispatched);
        Assert.Equal("ORDER-1", dispatched.OrderId);
    }

    [Fact]
    public void ProcessMessage_DuplicateMessageId_SkipsDispatch()
    {
        var bus = new EventBus();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var processor = new SqsMessageProcessor(bus, idempotencyStore);
        var dispatchCount = 0;
        bus.Subscribe<OrderPlaced>(_ => dispatchCount++);
        var envelope = EventTypeRegistry.Serialize(new OrderPlaced("ORDER-1", "SKU-1", 4, 199.99m));
        var rawBody = ToRawBody(envelope);
        processor.ProcessMessage(rawBody);

        processor.ProcessMessage(rawBody);

        Assert.Equal(1, dispatchCount);
    }
}
