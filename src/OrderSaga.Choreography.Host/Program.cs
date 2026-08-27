using Amazon.DynamoDBv2;
using Amazon.EventBridge;
using Amazon.S3;
using Amazon.SQS;
using Inventory.Domain;
using MongoDB.Driver;
using OrderSaga.Aws;
using OrderSaga.Choreography;
using OrderSaga.Choreography.Host;
using OrderSaga.Shared;
using Saga.Persistence;

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

builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var config = new AmazonS3Config();
    if (!string.IsNullOrWhiteSpace(localStackServiceUrl))
    {
        config.ServiceURL = localStackServiceUrl;
        config.ForcePathStyle = true; // LocalStack's S3 requires path-style bucket addressing.
    }

    return new AmazonS3Client(config);
});

var eventBusName = builder.Configuration["EventBridge:BusName"]
    ?? throw new InvalidOperationException("Configuration value 'EventBridge:BusName' is required.");

builder.Services.AddSingleton<IEventPublisher>(sp => new EventBridgeEventPublisher(
    sp.GetRequiredService<IAmazonEventBridge>(),
    eventBusName,
    sp.GetRequiredService<IHostApplicationLifetime>()));
builder.Services.AddSingleton<IIdempotencyStore, DynamoDbIdempotencyStore>();

// Mongo is the live InventoryItem event store; S3 is a best-effort secondary archive -- see
// docs/specs/saga-persistence.md. Config-driven so this stack's cluster/collection/bucket never
// collide with orchestration's separate ones. Registered as a DI singleton (rather than a raw
// local, as before) specifically so OutboxDrainerBackgroundService below can depend on it --
// AddHostedService factories run against the built container, so anything they need must be a
// registered service, not a local variable declared after Build().
var mongoConnectionString = builder.Configuration["Mongo:ConnectionString"]
    ?? throw new InvalidOperationException("Configuration value 'Mongo:ConnectionString' is required.");
var mongoDatabaseName = builder.Configuration["Mongo:DatabaseName"]
    ?? throw new InvalidOperationException("Configuration value 'Mongo:DatabaseName' is required.");
var inventoryEventsCollectionName = builder.Configuration["Mongo:InventoryEventsCollectionName"]
    ?? throw new InvalidOperationException("Configuration value 'Mongo:InventoryEventsCollectionName' is required.");
var archiveBucketName = builder.Configuration["S3:ArchiveBucketName"]
    ?? throw new InvalidOperationException("Configuration value 'S3:ArchiveBucketName' is required.");

builder.Services.AddSingleton<IInventoryEventStore>(sp =>
{
    var mongoDatabase = new MongoClient(mongoConnectionString).GetDatabase(mongoDatabaseName);
    return new S3ArchivingInventoryEventStore(
        new MongoInventoryEventStore(mongoDatabase, inventoryEventsCollectionName),
        new S3EventArchiveWriter(sp.GetRequiredService<IAmazonS3>(), archiveBucketName),
        sp.GetRequiredService<ILogger<S3ArchivingInventoryEventStore>>());
});

// Two separate buses, deliberately -- see InventoryParticipant's constructor doc comment.
// SqsMessageProcessor publishes only to `inboundBus`, which every participant subscribes to;
// participants publish their produced events only to `outboundBus`, which only
// OutboundEventForwarder subscribes to. Sharing one bus for both directions caused every
// participant to also react to sibling participants directly and synchronously, in-process,
// completely bypassing the SQS round-trip the spec calls for -- an unbounded republish loop,
// discovered only by actually running this against LocalStack, not by any unit test. Participants
// are wired below through InboundEventBus/OutboundEventBus (not the raw EventBus instances
// directly) so the compiler -- not just this comment -- rejects a future participant accidentally
// getting the two swapped. HostParticipantWiring's own tests exercise this exact composition.
var inboundBus = new EventBus();
var outboundBus = new EventBus();

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

builder.Services.AddHostedService(sp => new OutboxDrainerBackgroundService(
    sp.GetRequiredService<IInventoryEventStore>(),
    sp.GetRequiredService<IEventPublisher>(),
    sp.GetRequiredService<ILogger<OutboxDrainerBackgroundService>>()));

var app = builder.Build();

var eventStore = app.Services.GetRequiredService<IInventoryEventStore>();

// Seed this SKU's history once, only on true first-run (an empty collection) -- InventoryParticipant
// reloads state fresh from eventStore on every command from here on (see its ApplyWithRetry), so
// there's no in-memory item to build or hand off; this is purely about the durable store's
// first-ever write.
const string sku = "SKU-1";
var existingEvents = await eventStore.LoadEventsAsync(sku, CancellationToken.None);
if (existingEvents.Count == 0)
{
    try
    {
        var seed = InventoryItem.Seed(sku, 100);
        await eventStore.AppendRangeAsync(sku, 0, seed.UncommittedEvents, CancellationToken.None);
    }
    catch (ConcurrencyConflictException)
    {
        // The other desired_count instance cold-started at the same moment and seeded first --
        // that's fine, there's nothing left for this instance to do here.
    }
}

// Constructed directly (not via DI) since their constructors' only real job is subscribing to
// inboundBus -- that subscription keeps them alive for as long as inboundBus does, which outlives
// the whole application via the SqsMessageProcessor singleton registered above.
HostParticipantWiring.Wire(new InboundEventBus(inboundBus), new OutboundEventBus(outboundBus), paymentDeclineThreshold: 500m, eventStore);

// Eagerly construct the forwarder so its EventBus subscription is wired up before any SQS message
// arrives -- it's never otherwise resolved from the container, since nothing calls its methods
// directly. Participants are already constructed above, directly.
_ = app.Services.GetRequiredService<OutboundEventForwarder>();

app.MapGet("/health", () => Results.Ok());

app.MapPost("/orders", (PlaceOrderRequest request, OrderIntakeHandler handler) =>
    handler.Handle(request) ? Results.Accepted() : Results.BadRequest());

app.Run();
