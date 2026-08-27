using OrderSaga.Shared;

namespace OrderSaga.Choreography.Host;

public sealed record PlaceOrderRequest(string OrderId, string Sku, int Quantity, decimal Amount);

/// <summary>
/// Logic behind the HTTP intake endpoint, kept out of Program.cs route registration so it's
/// testable without pulling in WebApplicationFactory/Microsoft.AspNetCore.Mvc.Testing.
/// </summary>
public sealed class OrderIntakeHandler
{
    private readonly IEventPublisher _publisher;

    public OrderIntakeHandler(IEventPublisher publisher)
    {
        _publisher = publisher;
    }

    /// <returns>false if the request was rejected as invalid (nothing published, caller should
    /// return a client error); true if it was published.</returns>
    public bool Handle(PlaceOrderRequest request)
    {
        if (request.Quantity <= 0 || request.Amount < 0)
        {
            return false;
        }

        _publisher.Publish(new OrderPlaced(request.OrderId, request.Sku, request.Quantity, request.Amount));
        return true;
    }
}
