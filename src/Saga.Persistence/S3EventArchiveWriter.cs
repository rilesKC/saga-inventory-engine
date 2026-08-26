using Amazon.S3;
using Amazon.S3.Model;

namespace Saga.Persistence;

/// <summary>
/// Writes one object per archived payload to a single S3 bucket, supplied at construction -- a
/// Host wires up one instance per stack's archive bucket. No independent logic worth unit-testing
/// in isolation; real put behavior is verified via LocalStack, same precedent as this project's
/// other thin AWS SDK wrappers (e.g. SqsMessagePublisher).
/// </summary>
public sealed class S3EventArchiveWriter : IEventArchiveWriter
{
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;

    public S3EventArchiveWriter(IAmazonS3 client, string bucketName)
    {
        _client = client;
        _bucketName = bucketName;
    }

    public Task PutAsync(string key, string payload, CancellationToken cancellationToken) =>
        _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            ContentBody = payload,
        }, cancellationToken);
}
