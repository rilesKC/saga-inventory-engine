using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using OrderSaga.Aws;

namespace OrderSaga.Orchestration.Messaging;

/// <summary>
/// Claims a message ID via a conditional PutItem (attribute_not_exists) -- the canonical AWS
/// idempotency-store pattern. No independent logic worth unit-testing in isolation; real
/// conditional-write behavior is verified via LocalStack. Table name is supplied by the caller
/// (configuration-driven), not hardcoded -- choreography's DynamoDbIdempotencyStore hardcoded its
/// table name, which the same class of drift risk found in its EventBridge bus name could just as
/// easily have hit here too.
/// </summary>
public sealed class DynamoDbIdempotencyStore : IIdempotencyStore
{
    private readonly IAmazonDynamoDB _client;
    private readonly string _tableName;

    public DynamoDbIdempotencyStore(IAmazonDynamoDB client, string tableName)
    {
        _client = client;
        _tableName = tableName;
    }

    public async Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.PutItemAsync(new PutItemRequest
            {
                TableName = _tableName,
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
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["MessageId"] = new AttributeValue { S = messageId },
            },
        }, cancellationToken);
}
