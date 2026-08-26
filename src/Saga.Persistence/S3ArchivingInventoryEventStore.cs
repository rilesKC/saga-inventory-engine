using System.Text.Json;
using Inventory.Domain;
using Microsoft.Extensions.Logging;

namespace Saga.Persistence;

/// <summary>
/// Dual-writes every appended event to a secondary S3 archive after the inner (Mongo) write
/// succeeds. Mongo is authoritative -- an inner-store failure propagates and the archive is never
/// attempted; an archive failure is logged and swallowed, never rethrown, since losing an S3 copy
/// isn't a correctness problem, just a weaker durability backstop. Matches the "Mongo live, S3
/// secondary archive" split from the Saga Persistence spec.
/// </summary>
public sealed class S3ArchivingInventoryEventStore : IInventoryEventStore
{
    private readonly IInventoryEventStore _inner;
    private readonly IEventArchiveWriter _archive;
    private readonly ILogger<S3ArchivingInventoryEventStore> _logger;

    public S3ArchivingInventoryEventStore(IInventoryEventStore inner, IEventArchiveWriter archive, ILogger<S3ArchivingInventoryEventStore> logger)
    {
        _inner = inner;
        _archive = archive;
        _logger = logger;
    }

    public async Task AppendRangeAsync(string sku, int expectedEventCount, IReadOnlyList<object> events, CancellationToken cancellationToken)
    {
        await _inner.AppendRangeAsync(sku, expectedEventCount, events, cancellationToken);

        foreach (var @event in events)
        {
            var key = $"{sku}/{@event.GetType().Name}/{Guid.NewGuid()}.json";

            try
            {
                var payload = JsonSerializer.Serialize(@event, @event.GetType());
                await _archive.PutAsync(key, payload, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to archive inventory event {Key} to S3; Mongo write already succeeded.", key);
            }
        }
    }

    public Task<IReadOnlyList<object>> LoadEventsAsync(string sku, CancellationToken cancellationToken) =>
        _inner.LoadEventsAsync(sku, cancellationToken);
}
