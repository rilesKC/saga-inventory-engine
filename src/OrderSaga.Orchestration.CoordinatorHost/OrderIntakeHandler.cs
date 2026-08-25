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

    public void Handle(PlaceOrderRequest request)
    {
        _publisher.Publish(new OrderPlaced(request.OrderId, request.Sku, request.Quantity, request.Amount));
    }
}
