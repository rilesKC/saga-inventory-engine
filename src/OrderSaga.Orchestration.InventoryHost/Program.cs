using Amazon.DynamoDBv2;
using Amazon.SQS;
using Inventory.Domain;
using Microsoft.Extensions.Hosting;
using OrderSaga.Orchestration.InventoryHost;
using OrderSaga.Orchestration.Messaging;
using OrderSaga.Shared;

var builder = Host.CreateApplicationBuilder(args);

// Optional LocalStack override: when set, every AWS SDK client targets this endpoint instead of
// resolving real AWS credentials/region.
var localStackServiceUrl = builder.Configuration["Aws:ServiceUrl"];

builder.Services.AddSingleton<IAmazonSQS>(_ => AwsClientFactory.CreateSqsClient(localStackServiceUrl));
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => AwsClientFactory.CreateDynamoDbClient(localStackServiceUrl));

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

// No persistence yet (deferred, same as choreography) -- seed a fixed starting inventory for the
// demo SKU on every process start.
var items = new Dictionary<string, InventoryItem>
{
    ["SKU-1"] = InventoryItem.Seed("SKU-1", 100),
};

InventoryWiring.Wire(new InboundEventBus(inboundBus), new OutboundEventBus(outboundBus), items);

builder.Services.AddSingleton(sp => new SqsMessageProcessor(inboundBus, sp.GetRequiredService<IIdempotencyStore>()));
builder.Services.AddHostedService(sp => new SqsPollingBackgroundService(
    sp.GetRequiredService<IAmazonSQS>(),
    sp.GetRequiredService<SqsMessageProcessor>(),
    inventoryCommandsQueueUrl,
    sp.GetRequiredService<ILogger<SqsPollingBackgroundService>>()));

var host = builder.Build();

// The outbound forwarder is constructed here (after the container exists) since it needs
// IHostApplicationLifetime for the publisher it wraps -- same reasoning as CoordinatorHost.
var sqsClient = host.Services.GetRequiredService<IAmazonSQS>();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
var coordinatorInboundPublisher = new SqsMessagePublisher(sqsClient, coordinatorInboundQueueUrl, lifetime);
_ = new OutboundMessageForwarder(outboundBus, _ => coordinatorInboundPublisher);

host.Run();
