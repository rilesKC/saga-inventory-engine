namespace OrderSaga.Shared;

/// <summary>
/// Compile-time-distinct, publish-only view of an <see cref="EventBus"/>. See
/// <see cref="InboundEventBus"/>.
/// </summary>
public sealed class OutboundEventBus
{
    private readonly EventBus _bus;

    public OutboundEventBus(EventBus bus) => _bus = bus;

    public void Publish(object @event) => _bus.Publish(@event);
}
