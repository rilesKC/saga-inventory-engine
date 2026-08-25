namespace OrderSaga.Orchestration.Messaging;

public interface IIdempotencyStore
{
    Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken);

    Task ReleaseAsync(string messageId, CancellationToken cancellationToken);
}
