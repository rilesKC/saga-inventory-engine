using Amazon.DynamoDBv2;
using Amazon.SQS;
using Microsoft.Extensions.Hosting;
using OrderSaga.Orchestration.Messaging;
using OrderSaga.Orchestration.ResponderHost;
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
var statelessResponderCommandsQueueUrl = builder.Configuration["Sqs:StatelessResponderCommandsQueueUrl"]
    ?? throw new InvalidOperationException("Configuration value 'Sqs:StatelessResponderCommandsQueueUrl' is required.");

// Two separate buses, deliberately -- see PaymentResponder/ShippingResponder's constructor doc
// comments.
var inboundBus = new EventBus();
var outboundBus = new EventBus();

ResponderWiring.Wire(new InboundEventBus(inboundBus), new OutboundEventBus(outboundBus), paymentDeclineThreshold: 500m);

builder.Services.AddSingleton(sp => new SqsMessageProcessor(inboundBus, sp.GetRequiredService<IIdempotencyStore>()));
builder.Services.AddHostedService(sp => new SqsPollingBackgroundService(
    sp.GetRequiredService<IAmazonSQS>(),
    sp.GetRequiredService<SqsMessageProcessor>(),
    statelessResponderCommandsQueueUrl,
    sp.GetRequiredService<ILogger<SqsPollingBackgroundService>>()));

var host = builder.Build();

// The outbound forwarder is constructed here (after the container exists) since it needs
// IHostApplicationLifetime for the publisher it wraps -- same reasoning as CoordinatorHost.
var sqsClient = host.Services.GetRequiredService<IAmazonSQS>();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
var coordinatorInboundPublisher = new SqsMessagePublisher(sqsClient, coordinatorInboundQueueUrl, lifetime);
_ = new OutboundMessageForwarder(outboundBus, _ => coordinatorInboundPublisher);

host.Run();
