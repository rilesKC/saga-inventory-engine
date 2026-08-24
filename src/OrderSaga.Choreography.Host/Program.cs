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

builder.Services.AddSingleton<EventBus>();

// No persistence yet (deferred per spec) -- seed a fixed starting inventory for the demo SKU on
// every process start.
builder.Services.AddSingleton(_ => new Dictionary<string, InventoryItem>
{
    ["SKU-1"] = InventoryItem.Seed("SKU-1", 100),
});

builder.Services.AddSingleton<InventoryParticipant>();
builder.Services.AddSingleton(sp => new PaymentStub(sp.GetRequiredService<EventBus>(), threshold: 500m));
builder.Services.AddSingleton<ShippingStub>();
builder.Services.AddSingleton<OutboundEventForwarder>();

builder.Services.AddSingleton<SqsMessageProcessor>();
builder.Services.AddSingleton<OrderIntakeHandler>();

var queueUrl = builder.Configuration["Sqs:QueueUrl"]
    ?? throw new InvalidOperationException("Configuration value 'Sqs:QueueUrl' is required.");

builder.Services.AddHostedService(sp => new SqsPollingBackgroundService(
    sp.GetRequiredService<IAmazonSQS>(),
    sp.GetRequiredService<SqsMessageProcessor>(),
    queueUrl,
    sp.GetRequiredService<ILogger<SqsPollingBackgroundService>>()));

var app = builder.Build();

// Eagerly construct the participants and forwarder so their EventBus subscriptions are wired up
// before any request or SQS message arrives -- they're never otherwise resolved from the
// container, since nothing calls their methods directly.
_ = app.Services.GetRequiredService<InventoryParticipant>();
_ = app.Services.GetRequiredService<PaymentStub>();
_ = app.Services.GetRequiredService<ShippingStub>();
_ = app.Services.GetRequiredService<OutboundEventForwarder>();

app.MapPost("/orders", (PlaceOrderRequest request, OrderIntakeHandler handler) =>
{
    handler.Handle(request);
    return Results.Accepted();
});

app.Run();
