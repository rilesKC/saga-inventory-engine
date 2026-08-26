using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrderSaga.Orchestration;

namespace Saga.Persistence;

/// <summary>
/// Dual-writes every saved SagaState to a secondary S3 archive after the inner (Mongo) write
/// succeeds -- same shape and reasoning as S3ArchivingInventoryEventStore: Mongo is authoritative,
/// the archive write is best-effort and never blocks or fails the save.
/// </summary>
public sealed class S3ArchivingSagaStateStore : ISagaStateStore
{
    private readonly ISagaStateStore _inner;
    private readonly IEventArchiveWriter _archive;
    private readonly ILogger<S3ArchivingSagaStateStore> _logger;

    public S3ArchivingSagaStateStore(ISagaStateStore inner, IEventArchiveWriter archive, ILogger<S3ArchivingSagaStateStore> logger)
    {
        _inner = inner;
        _archive = archive;
        _logger = logger;
    }

    public async Task SaveAsync(SagaState state, int expectedVersion, CancellationToken cancellationToken)
    {
        await _inner.SaveAsync(state, expectedVersion, cancellationToken);

        var key = $"{state.OrderId}/{Guid.NewGuid()}.json";

        try
        {
            var payload = JsonSerializer.Serialize(state);
            await _archive.PutAsync(key, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to archive saga state {Key} to S3; Mongo write already succeeded.", key);
        }
    }

    public Task<SagaState?> TryLoadAsync(string orderId, CancellationToken cancellationToken) =>
        _inner.TryLoadAsync(orderId, cancellationToken);

    public Task<IReadOnlyList<SagaState>> LoadAllAsync(CancellationToken cancellationToken) =>
        _inner.LoadAllAsync(cancellationToken);
}
