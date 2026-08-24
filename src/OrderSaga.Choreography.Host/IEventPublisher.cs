namespace OrderSaga.Choreography.Host;

public interface IEventPublisher
{
    void Publish(object @event);
}
