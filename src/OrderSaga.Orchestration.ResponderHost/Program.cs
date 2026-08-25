using Amazon.DynamoDBv2;
using Amazon.SQS;
using Microsoft.Extensions.Hosting;
using OrderSaga.Aws;
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

builder.Services.AddSingleton<IMessagePublisher>(sp =>
    new SqsMessagePublisher(sp.GetRequiredService<IAmazonSQS>(), coordinatorInboundQueueUrl, sp.GetRequiredService<IHostApplicationLifetime>()));
builder.Services.AddSingleton(sp => new OutboundMessageForwarder(outboundBus, _ => sp.GetRequiredService<IMessagePublisher>()));

var host = builder.Build();

// Eagerly resolve the forwarder so its EventBus subscription is wired up before any responder
// reply arrives -- it's never otherwise resolved from the container, since nothing calls its
// methods directly. Same precedent as choreography's OutboundEventForwarder.
_ = host.Services.GetRequiredService<OutboundMessageForwarder>();

host.Run();
