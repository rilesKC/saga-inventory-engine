using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Amazon.SQS;

namespace OrderSaga.Orchestration.Messaging;

/// <summary>
/// Shared LocalStack-override-aware AWS SDK client construction -- built once here specifically so
/// it isn't tripled across CoordinatorHost/InventoryHost/ResponderHost the way choreography's
/// inline per-client pattern would have been if copied three times.
/// </summary>
public static class AwsClientFactory
{
    public static IAmazonSQS CreateSqsClient(string? localStackServiceUrl)
    {
        var config = new AmazonSQSConfig();
        ApplyLocalStackOverride(config, localStackServiceUrl);
        return new AmazonSQSClient(config);
    }

    public static IAmazonDynamoDB CreateDynamoDbClient(string? localStackServiceUrl)
    {
        var config = new AmazonDynamoDBConfig();
        ApplyLocalStackOverride(config, localStackServiceUrl);
        return new AmazonDynamoDBClient(config);
    }

    private static void ApplyLocalStackOverride(ClientConfig config, string? localStackServiceUrl)
    {
        if (!string.IsNullOrWhiteSpace(localStackServiceUrl))
        {
            config.ServiceURL = localStackServiceUrl;
        }
    }
}
