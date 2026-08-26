using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.SQS;
using Inventory.Domain;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using OrderSaga.Aws;
using OrderSaga.Orchestration.InventoryHost;
using OrderSaga.Orchestration.Messaging;
using OrderSaga.Shared;
using Saga.Persistence;

var builder = Host.CreateApplicationBuilder(args);

// Optional LocalStack override: when set, every AWS SDK client targets this endpoint instead of
// resolving real AWS credentials/region.
var localStackServiceUrl = builder.Configuration["Aws:ServiceUrl"];

builder.Services.AddSingleton<IAmazonSQS>(_ => AwsClientFactory.CreateSqsClient(localStackServiceUrl));
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => AwsClientFactory.CreateDynamoDbClient(localStackServiceUrl));
builder.Services.AddSingleton<IAmazonS3>(_ => AwsClientFactory.CreateS3Client(localStackServiceUrl));

var idempotencyTableName = builder.Configuration["Dynamo:IdempotencyTableName"]
    ?? throw new InvalidOperationException("Configuration value 'Dynamo:IdempotencyTableName' is required.");

builder.Services.AddSingleton<IIdempotencyStore>(sp =>
    new DynamoDbIdempotencyStore(sp.GetRequiredService<IAmazonDynamoDB>(), idempotencyTableName));

var coordinatorInboundQueueUrl = builder.Configuration["Sqs:CoordinatorInboundQueueUrl"]
    ?? throw new InvalidOperationException("Configuration value 'Sqs:CoordinatorInboundQueueUrl' is required.");
var inventoryCommandsQueueUrl = builder.Configuration["Sqs:InventoryCommandsQueueUrl"]
    ?? throw new InvalidOperationException("Configuration value 'Sqs:InventoryCommandsQueueUrl' is required.");

// Two separate buses, deliberately -- see InventoryResponder's constructor doc comment.
var inboundBus = new EventBus();
var outboundBus = new EventBus();

builder.Services.AddSingleton(sp => new SqsMessageProcessor(inboundBus, sp.GetRequiredService<IIdempotencyStore>()));
builder.Services.AddHostedService(sp => new SqsPollingBackgroundService(
    sp.GetRequiredService<IAmazonSQS>(),
    sp.GetRequiredService<SqsMessageProcessor>(),
    inventoryCommandsQueueUrl,
    sp.GetRequiredService<ILogger<SqsPollingBackgroundService>>()));

builder.Services.AddSingleton<IMessagePublisher>(sp =>
    new SqsMessagePublisher(sp.GetRequiredService<IAmazonSQS>(), coordinatorInboundQueueUrl, sp.GetRequiredService<IHostApplicationLifetime>()));
builder.Services.AddSingleton(sp => new OutboundMessageForwarder(outboundBus, _ => sp.GetRequiredService<IMessagePublisher>()));

var host = builder.Build();

// Mongo is the live InventoryItem event store; S3 is a best-effort secondary archive -- see
// docs/specs/saga-persistence.md. Config-driven so this stack's cluster/collection/bucket never
// collide with choreography's separate ones. Constructed directly (not via DI) since it's needed
// before the container's hosted services start, to load-or-seed InventoryItem below.
var mongoConnectionString = builder.Configuration["Mongo:ConnectionString"]
    ?? throw new InvalidOperationException("Configuration value 'Mongo:ConnectionString' is required.");
var mongoDatabaseName = builder.Configuration["Mongo:DatabaseName"]
    ?? throw new InvalidOperationException("Configuration value 'Mongo:DatabaseName' is required.");
var inventoryEventsCollectionName = builder.Configuration["Mongo:InventoryEventsCollectionName"]
    ?? throw new InvalidOperationException("Configuration value 'Mongo:InventoryEventsCollectionName' is required.");
var archiveBucketName = builder.Configuration["S3:ArchiveBucketName"]
    ?? throw new InvalidOperationException("Configuration value 'S3:ArchiveBucketName' is required.");

var mongoDatabase = new MongoClient(mongoConnectionString).GetDatabase(mongoDatabaseName);
var eventStore = new S3ArchivingInventoryEventStore(
    new MongoInventoryEventStore(mongoDatabase, inventoryEventsCollectionName),
    new S3EventArchiveWriter(host.Services.GetRequiredService<IAmazonS3>(), archiveBucketName),
    host.Services.GetRequiredService<ILogger<S3ArchivingInventoryEventStore>>());

// Seed this SKU's history once, only on true first-run (an empty collection) -- InventoryResponder
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

InventoryWiring.Wire(new InboundEventBus(inboundBus), new OutboundEventBus(outboundBus), eventStore);

// Eagerly resolve the forwarder so its EventBus subscription is wired up before any inventory
// reply arrives -- it's never otherwise resolved from the container, since nothing calls its
// methods directly. Same precedent as choreography's OutboundEventForwarder.
_ = host.Services.GetRequiredService<OutboundMessageForwarder>();

host.Run();
