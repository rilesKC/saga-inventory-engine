using Inventory.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Publishes events InventoryParticipant durably appended but didn't publish synchronously (see
/// its ApplyWithRetry doc comment) -- the read side of the outbox pattern that closes the
/// event-loss gap a crash between "durably appended" and "published" used to open. Polls
/// independently of the synchronous command-handling path, trading a small amount of latency
/// (other participants see the event on the next drain cycle, not the same tick) for the event
/// never being silently lost. DrainOnceAsync is public specifically so it's directly unit-testable
/// without needing to run the full ExecuteAsync loop or an InternalsVisibleTo entry.
/// </summary>
public sealed class OutboxDrainerBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IInventoryEventStore _eventStore;
    private readonly IEventPublisher _publisher;
    private readonly ILogger<OutboxDrainerBackgroundService> _logger;

    public OutboxDrainerBackgroundService(IInventoryEventStore eventStore, IEventPublisher publisher, ILogger<OutboxDrainerBackgroundService> logger)
    {
        _eventStore = eventStore;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failure here (e.g. LoadUnpublishedAsync itself) must not take the whole host
                // down -- same reasoning as SqsPollingBackgroundService's outer try/catch.
                _logger.LogError(ex, "Outbox drain cycle failed; retrying after the next poll.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    public async Task DrainOnceAsync(CancellationToken cancellationToken)
    {
        var pending = await _eventStore.LoadUnpublishedAsync(cancellationToken);

        foreach (var entry in pending)
        {
            try
            {
                // StockSeeded doesn't implement IOutboundEvent -- it was never meant to leave the
                // process (see the marker interface's own doc comment). Mark it published anyway so
                // it stops showing up in every future poll.
                if (entry.Event is IOutboundEvent)
                {
                    _publisher.Publish(entry.Event);
                }

                await _eventStore.MarkPublishedAsync(entry.Sku, entry.Sequence, cancellationToken);
            }
            catch (Exception ex)
            {
                // Deliberately left unmarked: the next drain cycle retries it. Same per-item
                // resilience shape as SqsPollingBackgroundService's per-message try/catch -- one
                // bad entry must not stop the rest of the batch from draining.
                _logger.LogWarning(ex, "Failed to drain outbox entry {Sku}/{Sequence}; leaving for retry.", entry.Sku, entry.Sequence);
            }
        }
    }
}
