using Amazon.DynamoDBv2;
using Amazon.SQS;
using OrderSaga.Orchestration.CoordinatorHost;
using OrderSaga.Orchestration.Messaging;
using OrderSaga.Shared;

var builder = WebApplication.CreateBuilder(args);

// Optional LocalStack override: when set, every AWS SDK client targets this endpoint instead of
// resolving real AWS credentials/region.
var localStackServiceUrl = builder.Configuration["Aws:ServiceUrl"];

builder.Services.AddSingleton<IAmazonSQS>(_ => AwsClientFactory.CreateSqsClient(localStackServiceUrl));
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => AwsClientFactory.CreateDynamoDbClient(localStackServiceUrl));

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

_ = CoordinatorWiring.Wire(new InboundEventBus(inboundBus), new OutboundEventBus(outboundBus));

builder.Services.AddSingleton(sp => new SqsMessageProcessor(inboundBus, sp.GetRequiredService<IIdempotencyStore>()));
builder.Services.AddHostedService(sp => new SqsPollingBackgroundService(
    sp.GetRequiredService<IAmazonSQS>(),
    sp.GetRequiredService<SqsMessageProcessor>(),
    coordinatorInboundQueueUrl,
    sp.GetRequiredService<ILogger<SqsPollingBackgroundService>>()));

var app = builder.Build();

// The publishers below, and everything built from them, are constructed here (after the container
// exists) rather than registered as DI services -- there's no clean way to express "three distinct
// IMessagePublisher instances, one per destination queue" through type-based DI resolution.
var sqsClient = app.Services.GetRequiredService<IAmazonSQS>();
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

var inventoryCommandsPublisher = new SqsMessagePublisher(sqsClient, inventoryCommandsQueueUrl, lifetime);
var statelessResponderCommandsPublisher = new SqsMessagePublisher(sqsClient, statelessResponderCommandsQueueUrl, lifetime);
var coordinatorInboundPublisher = new SqsMessagePublisher(sqsClient, coordinatorInboundQueueUrl, lifetime);

var commandRouter = new CommandRouter(inventoryCommandsPublisher, statelessResponderCommandsPublisher);
_ = new OutboundMessageForwarder(outboundBus, commandRouter.PublisherFor);

var orderIntakeHandler = new OrderIntakeHandler(coordinatorInboundPublisher);

app.MapGet("/health", () => Results.Ok());

app.MapPost("/orders", (PlaceOrderRequest request) =>
{
    orderIntakeHandler.Handle(request);
    return Results.Accepted();
});

app.Run();
