namespace OrderSaga.Shared;

public sealed class EventBus
{
    private readonly Dictionary<Type, List<Action<object>>> _handlers = [];
    private readonly Queue<object> _pending = new();
    private bool _isDispatching;

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

    // Breadth-first: every handler of an event finishes before any handler of an event that
    // handler published starts. A naive recursive/depth-first Publish would let a nested publish
    // run ahead of sibling handlers still waiting on the original event -- e.g. a second
    // OrderPlaced subscriber that hasn't run yet when a first subscriber's reaction to OrderPlaced
    // already publishes and fully processes a downstream event.
    public void Publish(object @event)
    {
        _pending.Enqueue(@event);
        if (_isDispatching)
        {
            return;
        }

        _isDispatching = true;
        try
        {
            while (_pending.Count > 0)
            {
                var next = _pending.Dequeue();
                if (_handlers.TryGetValue(next.GetType(), out var list))
                {
                    foreach (var handler in list.ToArray())
                    {
                        handler(next);
                    }
                }
            }
        }
        finally
        {
            // A handler exception aborts this while loop mid-way, potentially leaving events that
            // other handlers already enqueued (via their own nested Publish calls) still sitting in
            // _pending. Left uncleared, the next unrelated Publish call on this bus -- a singleton
            // for the host's lifetime in production -- would dequeue and dispatch them mixed into
            // that call's own dispatch cycle: side effects for this failed dispatch firing at the
            // wrong time, attributed to the wrong message. The caller that triggered this Publish
            // (SqsMessageProcessor) already treats any exception as "release the claim, redeliver
            // the whole message", which regenerates these same nested events from scratch -- so
            // discarding them here is correct, not just safe. On the normal, non-exception path
            // _pending is already empty by this point, so this is a no-op.
            _pending.Clear();
            _isDispatching = false;
        }
    }
}
