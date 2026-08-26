using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration;

public sealed class InventoryResponder
{
    private readonly InboundEventBus _inbound;
    private readonly OutboundEventBus _outbound;
    private readonly IInventoryEventStore _eventStore;

    /// <param name="inbound">Subscribed to for trigger commands.</param>
    /// <param name="outbound">Published to for produced replies. See
    /// <see cref="SagaCoordinator"/>'s constructor for why these are separate.</param>
    /// <param name="eventStore">Durable, shared source of truth for this SKU's InventoryItem --
    /// every command reloads the latest state from here immediately before mutating it (see
    /// ApplyWithRetry), rather than mutating one cached in-memory object. That reload is what makes
    /// desired_count >= 2 safe: two concurrently-running Host instances no longer each hold their
    /// own drifting copy of InventoryItem.</param>
    public InventoryResponder(InboundEventBus inbound, OutboundEventBus outbound, IInventoryEventStore eventStore)
    {
        _inbound = inbound;
        _outbound = outbound;
        _eventStore = eventStore;
        _inbound.Subscribe<ReserveStockCommand>(OnReserveStockCommand);
        _inbound.Subscribe<ConfirmReservationCommand>(OnConfirmReservationCommand);
        _inbound.Subscribe<ReleaseReservationCommand>(OnReleaseReservationCommand);
    }

    private void OnReserveStockCommand(ReserveStockCommand command)
    {
        try
        {
            // Amount is left unset here: unlike choreography's PaymentStub (which reads it directly
            // off Inventory.Domain's StockReserved, see PaymentStub.OnStockReserved), orchestration's
            // payment decision reads Amount from the coordinator's own persisted SagaState
            // (SagaCoordinator.OnStockReservedReply), so this bounded context never needs it.
            ApplyWithRetry(command.Sku, item => item.Handle(new ReserveStock(command.Sku, command.OrderId, command.Quantity, Amount: 0m)));
        }
        catch (InsufficientStockException)
        {
            _outbound.Publish(new StockReservationFailedReply(command.OrderId, command.Sku));
            return;
        }

        _outbound.Publish(new StockReservedReply(command.OrderId, command.Sku));
    }

    private void OnConfirmReservationCommand(ConfirmReservationCommand command)
    {
        ApplyWithRetry(command.Sku, item => item.Handle(new ConfirmReservation(command.Sku, command.OrderId)));
        _outbound.Publish(new ReservationConfirmedReply(command.OrderId, command.Sku));
    }

    private void OnReleaseReservationCommand(ReleaseReservationCommand command)
    {
        ApplyWithRetry(command.Sku, item => item.Handle(new ReleaseReservation(command.Sku, command.OrderId)));
        _outbound.Publish(new ReservationReleasedReply(command.OrderId, command.Sku));
    }

    /// <summary>
    /// Reloads InventoryItem from the durable event log, applies the mutation, and appends the
    /// resulting new event(s) guarded by optimistic concurrency -- retrying from a fresh reload if
    /// a concurrent writer (the other Host instance) already appended first. mutate is expected to
    /// throw for a domain-level rejection (e.g. InsufficientStockException); that propagates to the
    /// caller unchanged, without appending anything.
    /// </summary>
    private void ApplyWithRetry(string sku, Action<InventoryItem> mutate)
    {
        while (true)
        {
            var history = _eventStore.LoadEventsAsync(sku, CancellationToken.None).GetAwaiter().GetResult();
            var item = InventoryItem.LoadFromHistory(history);

            mutate(item);

            if (item.UncommittedEvents.Count == 0)
            {
                return;
            }

            try
            {
                _eventStore.AppendRangeAsync(sku, history.Count, item.UncommittedEvents, CancellationToken.None).GetAwaiter().GetResult();
            }
            // Fully qualified: this class's own namespace (OrderSaga.Orchestration) declares its own
            // ConcurrencyConflictException (see ConcurrencyConflictException.cs, used by
            // ISagaStateStore), which an unqualified reference here would bind to instead of the
            // Inventory.Domain one IInventoryEventStore actually throws -- same enclosing namespace
            // wins over `using Inventory.Domain;`, with no compiler ambiguity error. That silently
            // defeated this whole retry loop until caught by a full-effort code review.
            catch (Inventory.Domain.ConcurrencyConflictException)
            {
                continue;
            }

            return;
        }
    }
}
