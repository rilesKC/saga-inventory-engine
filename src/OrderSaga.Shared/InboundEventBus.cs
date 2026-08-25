namespace OrderSaga.Shared;

/// <summary>
/// Compile-time-distinct, subscribe-only view of an <see cref="EventBus"/>. Exists so a
/// participant's inbound/outbound constructor parameters can no longer be swapped by accident --
/// see <c>InventoryParticipant</c>'s constructor doc comment for the incident this was introduced
/// to stop from recurring.
/// </summary>
public sealed class InboundEventBus
{
    private readonly EventBus _bus;

    public InboundEventBus(EventBus bus) => _bus = bus;

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : notnull => _bus.Subscribe(handler);
}
