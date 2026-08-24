using Amazon.DynamoDBv2;
using Amazon.EventBridge;
using Amazon.SQS;
using Inventory.Domain;
using OrderSaga.Choreography;
using OrderSaga.Choreography.Host;
using OrderSaga.Shared;

var builder = WebApplication.CreateBuilder(args);

// Optional LocalStack override: when set, every AWS SDK client targets this endpoint instead of
// resolving real AWS credentials/region -- used for LocalStack validation (task 21), unset for a
// real deployment (task 22).
var localStackServiceUrl = builder.Configuration["Aws:ServiceUrl"];

builder.Services.AddSingleton<IAmazonEventBridge>(_ =>
{
    var config = new AmazonEventBridgeConfig();
    if (!string.IsNullOrWhiteSpace(localStackServiceUrl))
    {
        config.ServiceURL = localStackServiceUrl;
    }

    return new AmazonEventBridgeClient(config);
});

builder.Services.AddSingleton<IAmazonSQS>(_ =>
{
    var config = new AmazonSQSConfig();
    if (!string.IsNullOrWhiteSpace(localStackServiceUrl))
    {
        config.ServiceURL = localStackServiceUrl;
    }

    return new AmazonSQSClient(config);
});

builder.Services.AddSingleton<IAmazonDynamoDB>(_ =>
{
    var config = new AmazonDynamoDBConfig();
    if (!string.IsNullOrWhiteSpace(localStackServiceUrl))
    {
        config.ServiceURL = localStackServiceUrl;
    }

    return new AmazonDynamoDBClient(config);
});

builder.Services.AddSingleton<IEventPublisher, EventBridgeEventPublisher>();
builder.Services.AddSingleton<IIdempotencyStore, DynamoDbIdempotencyStore>();

// Two separate buses, deliberately -- see InventoryParticipant's constructor doc comment.
// SqsMessageProcessor publishes only to `inboundBus`, which every participant subscribes to;
// participants publish their produced events only to `outboundBus`, which only
// OutboundEventForwarder subscribes to. Sharing one bus for both directions caused every
// participant to also react to sibling participants directly and synchronously, in-process,
// completely bypassing the SQS round-trip the spec calls for -- an unbounded republish loop,
// discovered only by actually running this against LocalStack, not by any unit test.
var inboundBus = new EventBus();
var outboundBus = new EventBus();

// No persistence yet (deferred per spec) -- seed a fixed starting inventory for the demo SKU on
// every process start.
var items = new Dictionary<string, InventoryItem>
{
    ["SKU-1"] = InventoryItem.Seed("SKU-1", 100),
};

// Constructed directly (not via DI) since their constructors' only real job is subscribing to
// inboundBus -- that subscription keeps them alive for as long as inboundBus does, which outlives
// the whole application via the SqsMessageProcessor singleton below.
_ = new InventoryParticipant(inboundBus, outboundBus, items);
_ = new PaymentStub(inboundBus, outboundBus, threshold: 500m);
_ = new ShippingStub(inboundBus, outboundBus);

builder.Services.AddSingleton(sp => new OutboundEventForwarder(outboundBus, sp.GetRequiredService<IEventPublisher>()));
builder.Services.AddSingleton(sp => new SqsMessageProcessor(inboundBus, sp.GetRequiredService<IIdempotencyStore>()));
builder.Services.AddSingleton<OrderIntakeHandler>();

var queueUrl = builder.Configuration["Sqs:QueueUrl"]
    ?? throw new InvalidOperationException("Configuration value 'Sqs:QueueUrl' is required.");

builder.Services.AddHostedService(sp => new SqsPollingBackgroundService(
    sp.GetRequiredService<IAmazonSQS>(),
    sp.GetRequiredService<SqsMessageProcessor>(),
    queueUrl,
    sp.GetRequiredService<ILogger<SqsPollingBackgroundService>>()));

var app = builder.Build();

// Eagerly construct the forwarder so its EventBus subscription is wired up before any SQS message
// arrives -- it's never otherwise resolved from the container, since nothing calls its methods
// directly. Participants are already constructed above, directly.
_ = app.Services.GetRequiredService<OutboundEventForwarder>();

app.MapGet("/health", () => Results.Ok());

app.MapPost("/orders", (PlaceOrderRequest request, OrderIntakeHandler handler) =>
{
    handler.Handle(request);
    return Results.Accepted();
});

app.Run();
