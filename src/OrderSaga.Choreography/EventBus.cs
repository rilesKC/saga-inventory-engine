namespace OrderSaga.Choreography;

public sealed class EventBus
{
    private readonly Dictionary<Type, List<Action<object>>> _handlers = [];

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : notnull
    {
        var eventType = typeof(TEvent);
        if (!_handlers.TryGetValue(eventType, out var list))
        {
            list = [];
            _handlers[eventType] = list;
        }

        list.Add(e => handler((TEvent)e));
    }

    public void Publish(object @event)
    {
        if (_handlers.TryGetValue(@event.GetType(), out var list))
        {
            foreach (var handler in list.ToArray())
            {
                handler(@event);
            }
        }
    }
}
