namespace OrderSaga.Orchestration.Messaging;

public interface IMessagePublisher
{
    void Publish(object message);
}
