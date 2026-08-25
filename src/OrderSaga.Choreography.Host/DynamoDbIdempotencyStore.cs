using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Claims a message ID via a conditional PutItem (attribute_not_exists) -- the canonical AWS
/// idempotency-store pattern. No independent logic worth unit-testing in isolation; real
/// conditional-write behavior is verified via LocalStack.
/// </summary>
public sealed class DynamoDbIdempotencyStore : IIdempotencyStore
{
    private const string TableName = "order-saga-choreography-idempotency";

    private readonly IAmazonDynamoDB _client;

    public DynamoDbIdempotencyStore(IAmazonDynamoDB client)
    {
        _client = client;
    }

    public async Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.PutItemAsync(new PutItemRequest
            {
                TableName = TableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["MessageId"] = new AttributeValue { S = messageId },
                },
                ConditionExpression = "attribute_not_exists(MessageId)",
            }, cancellationToken);

            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public Task ReleaseAsync(string messageId, CancellationToken cancellationToken) =>
        _client.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["MessageId"] = new AttributeValue { S = messageId },
            },
        }, cancellationToken);
}
