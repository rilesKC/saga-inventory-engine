using Amazon.DynamoDBv2;
using Amazon.SQS;
using OrderSaga.Aws;
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

var app = builder.Build();

// Eagerly resolve the forwarder so its EventBus subscription is wired up before any command reply
// arrives -- it's never otherwise resolved from the container, since nothing calls its methods
// directly. Same precedent as choreography's OutboundEventForwarder.
_ = app.Services.GetRequiredService<OutboundMessageForwarder>();

app.MapGet("/health", () => Results.Ok());

app.MapPost("/orders", (PlaceOrderRequest request, OrderIntakeHandler handler) =>
{
    handler.Handle(request);
    return Results.Accepted();
});

app.Run();
