using OrderSaga.Orchestration.Messaging;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.CoordinatorHost;

public sealed record PlaceOrderRequest(string OrderId, string Sku, int Quantity, decimal Amount);

/// <summary>
/// Logic behind the HTTP intake endpoint, kept out of Program.cs route registration so it's
/// testable without pulling in WebApplicationFactory/Microsoft.AspNetCore.Mvc.Testing. Publishes
/// via IMessagePublisher directly onto coordinator-inbound -- the same "every message takes the
/// real transport path" principle choreography's Host used, adapted for a transport that's
/// SQS-only (no EventBridge to publish through instead).
/// </summary>
public sealed class OrderIntakeHandler
{
    private readonly IMessagePublisher _publisher;

    public OrderIntakeHandler(IMessagePublisher publisher)
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
