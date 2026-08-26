using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using OrderSaga.Aws;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Claims a message ID via a conditional PutItem (attribute_not_exists(MessageId) OR
/// ExpiresAt &lt; now) -- the canonical AWS idempotency-store pattern, extended with a claim TTL.
/// No independent logic worth unit-testing in isolation; real conditional-write behavior is
/// verified via LocalStack. Table name stays a hardcoded constant here rather than a constructor
/// parameter (unlike orchestration's DynamoDbIdempotencyStore) -- a deliberate, already-reviewed
/// choice, not an oversight; see infra/variables.tf's comment.
/// </summary>
public sealed class DynamoDbIdempotencyStore : IIdempotencyStore
{
    private const string TableName = "order-saga-choreography-idempotency";

    // A claim orphaned by a process crash (nothing throws, so ReleaseAsync never runs) must
    // become reclaimable within this queue's own redrive budget (default 30s visibility timeout x
    // 3 max receives = ~90s, see infra/modules/messaging/variables.tf) for a redelivery to have a
    // real chance of succeeding before the message moves to the DLQ. 60s comfortably outlasts this
    // store's own normal claim-to-completion time (a handful of local Mongo calls) while still
    // leaving room for at least one more delivery attempt after an orphan.
    private static readonly TimeSpan ClaimTtl = TimeSpan.FromSeconds(60);

    private readonly IAmazonDynamoDB _client;

    public DynamoDbIdempotencyStore(IAmazonDynamoDB client)
    {
        _client = client;
    }

    public async Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        try
        {
            await _client.PutItemAsync(new PutItemRequest
            {
                TableName = TableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["MessageId"] = new AttributeValue { S = messageId },
                    ["ExpiresAt"] = new AttributeValue { N = now.Add(ClaimTtl).ToUnixTimeSeconds().ToString() },
                },
                ConditionExpression = "attribute_not_exists(MessageId) OR ExpiresAt < :now",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":now"] = new AttributeValue { N = now.ToUnixTimeSeconds().ToString() },
                },
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
