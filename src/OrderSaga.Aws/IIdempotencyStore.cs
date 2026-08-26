namespace OrderSaga.Aws;

public interface IIdempotencyStore
{
    /// <summary>
    /// Claims messageId if it isn't already claimed. A claim orphaned by a process crash (the
    /// claimer dies before calling <see cref="ReleaseAsync"/> or completing normally) is expected
    /// to become reclaimable again after an implementation-defined expiry, rather than blocking
    /// that message from ever being reprocessed -- see DynamoDbIdempotencyStore for the concrete
    /// policy.
    /// </summary>
    Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken);

    Task ReleaseAsync(string messageId, CancellationToken cancellationToken);
}
