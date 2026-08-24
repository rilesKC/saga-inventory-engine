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

    public void Handle(PlaceOrderRequest request)
    {
        _publisher.Publish(new OrderPlaced(request.OrderId, request.Sku, request.Quantity, request.Amount));
    }
}
