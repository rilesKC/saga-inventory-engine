using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.SQS;
using Inventory.Domain;
using MongoDB.Driver;
using OrderSaga.Aws;
using OrderSaga.Orchestration;
using OrderSaga.Orchestration.CoordinatorHost;
using OrderSaga.Orchestration.Messaging;
using OrderSaga.Shared;
using Saga.Persistence;

var builder = WebApplication.CreateBuilder(args);

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

var inventoryCommandsQueueUrl = builder.Configuration["Sqs:InventoryCommandsQueueUrl"]
    ?? throw new InvalidOperationException("Configuration value 'Sqs:InventoryCommandsQueueUrl' is required.");
var statelessResponderCommandsQueueUrl = builder.Configuration["Sqs:StatelessResponderCommandsQueueUrl"]
    ?? throw new InvalidOperationException("Configuration value 'Sqs:StatelessResponderCommandsQueueUrl' is required.");
var coordinatorInboundQueueUrl = builder.Configuration["Sqs:CoordinatorInboundQueueUrl"]
    ?? throw new InvalidOperationException("Configuration value 'Sqs:CoordinatorInboundQueueUrl' is required.");

// Two separate buses, deliberately -- see SagaCoordinator's constructor doc comment. Sharing one
// would let the coordinator's own issued commands loop back through its own reply subscriptions
// in-process, bypassing the real SQS round-trip -- the same class of bug choreography's Host was
// fixed for.
var inboundBus = new EventBus();
var outboundBus = new EventBus();

builder.Services.AddSingleton(sp => new SqsMessageProcessor(inboundBus, sp.GetRequiredService<IIdempotencyStore>()));
builder.Services.AddHostedService(sp => new SqsPollingBackgroundService(
    sp.GetRequiredService<IAmazonSQS>(),
    sp.GetRequiredService<SqsMessageProcessor>(),
    coordinatorInboundQueueUrl,
    sp.GetRequiredService<ILogger<SqsPollingBackgroundService>>()));

// Keyed singletons -- .NET's type-based DI can't disambiguate three same-typed IMessagePublisher
// instances, one per destination queue, but a service key can.
builder.Services.AddKeyedSingleton<IMessagePublisher>("inventory-commands", (sp, _) =>
    new SqsMessagePublisher(sp.GetRequiredService<IAmazonSQS>(), inventoryCommandsQueueUrl, sp.GetRequiredService<IHostApplicationLifetime>()));
builder.Services.AddKeyedSingleton<IMessagePublisher>("stateless-responder-commands", (sp, _) =>
    new SqsMessagePublisher(sp.GetRequiredService<IAmazonSQS>(), statelessResponderCommandsQueueUrl, sp.GetRequiredService<IHostApplicationLifetime>()));
builder.Services.AddKeyedSingleton<IMessagePublisher>("coordinator-inbound", (sp, _) =>
    new SqsMessagePublisher(sp.GetRequiredService<IAmazonSQS>(), coordinatorInboundQueueUrl, sp.GetRequiredService<IHostApplicationLifetime>()));

builder.Services.AddSingleton(sp => new CommandRouter(
    sp.GetRequiredKeyedService<IMessagePublisher>("inventory-commands"),
    sp.GetRequiredKeyedService<IMessagePublisher>("stateless-responder-commands")));

builder.Services.AddSingleton(sp => new OutboundMessageForwarder(outboundBus, sp.GetRequiredService<CommandRouter>().PublisherFor));

builder.Services.AddSingleton(sp => new OrderIntakeHandler(sp.GetRequiredKeyedService<IMessagePublisher>("coordinator-inbound")));

// Mongo is the live SagaState snapshot store; S3 is a best-effort secondary archive -- see
// docs/specs/saga-persistence.md. Same Mongo cluster/database as the Inventory responder's
// InventoryHost, a different collection (SagaState isn't event-sourced, so it doesn't share
// InventoryItem's append-only shape). Config-driven so this stack's values never collide with
// choreography's separate ones.
var mongoConnectionString = builder.Configuration["Mongo:ConnectionString"]
    ?? throw new InvalidOperationException("Configuration value 'Mongo:ConnectionString' is required.");
var mongoDatabaseName = builder.Configuration["Mongo:DatabaseName"]
    ?? throw new InvalidOperationException("Configuration value 'Mongo:DatabaseName' is required.");
var sagaStateCollectionName = builder.Configuration["Mongo:SagaStateCollectionName"]
    ?? throw new InvalidOperationException("Configuration value 'Mongo:SagaStateCollectionName' is required.");
// Not otherwise needed by this host's saga-coordination flow -- read only so OrderLookupHandler
// can filter Inventory history down to one order, same Mongo cluster InventoryHost already writes.
var inventoryEventsCollectionName = builder.Configuration["Mongo:InventoryEventsCollectionName"]
    ?? throw new InvalidOperationException("Configuration value 'Mongo:InventoryEventsCollectionName' is required.");
var archiveBucketName = builder.Configuration["S3:ArchiveBucketName"]
    ?? throw new InvalidOperationException("Configuration value 'S3:ArchiveBucketName' is required.");

// Shared by both stores below so they use one MongoClient (one connection pool) against the
// cluster, not one each.
builder.Services.AddSingleton(_ => new MongoClient(mongoConnectionString).GetDatabase(mongoDatabaseName));

// Promoted from a local variable to a registered singleton (was previously constructed only after
// builder.Build(), passed directly into CoordinatorWiring.Wire below) so OrderLookupHandler can
// receive it via constructor injection, matching OrderIntakeHandler's existing DI convention.
builder.Services.AddSingleton<ISagaStateStore>(sp => new S3ArchivingSagaStateStore(
    new MongoSagaStateStore(sp.GetRequiredService<IMongoDatabase>(), sagaStateCollectionName),
    new S3EventArchiveWriter(sp.GetRequiredService<IAmazonS3>(), archiveBucketName),
    sp.GetRequiredService<ILogger<S3ArchivingSagaStateStore>>()));

builder.Services.AddSingleton<IInventoryEventStore>(sp => new S3ArchivingInventoryEventStore(
    new MongoInventoryEventStore(sp.GetRequiredService<IMongoDatabase>(), inventoryEventsCollectionName),
    new S3EventArchiveWriter(sp.GetRequiredService<IAmazonS3>(), archiveBucketName),
    sp.GetRequiredService<ILogger<S3ArchivingInventoryEventStore>>()));

builder.Services.AddSingleton<OrderLookupHandler>();

var app = builder.Build();

var sagaStateStore = app.Services.GetRequiredService<ISagaStateStore>();

_ = CoordinatorWiring.Wire(new InboundEventBus(inboundBus), new OutboundEventBus(outboundBus), sagaStateStore);

// Eagerly resolve the forwarder so its EventBus subscription is wired up before any command reply
// arrives -- it's never otherwise resolved from the container, since nothing calls its methods
// directly. Same precedent as choreography's OutboundEventForwarder.
_ = app.Services.GetRequiredService<OutboundMessageForwarder>();

app.MapGet("/health", () => Results.Ok());

app.MapPost("/orders", (PlaceOrderRequest request, OrderIntakeHandler handler) =>
    handler.Handle(request) ? Results.Accepted() : Results.BadRequest());

app.MapGet("/orders/{id}", async (string id, OrderLookupHandler handler, CancellationToken cancellationToken) =>
{
    var details = await handler.HandleAsync(id, cancellationToken);
    return details is null ? Results.NotFound() : Results.Ok(details);
});

app.Run();
