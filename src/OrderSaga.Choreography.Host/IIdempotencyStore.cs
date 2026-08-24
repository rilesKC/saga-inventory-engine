namespace OrderSaga.Choreography.Host;

public interface IIdempotencyStore
{
    bool TryClaim(string messageId);
}
