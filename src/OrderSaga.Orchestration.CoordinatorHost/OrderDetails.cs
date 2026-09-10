using Inventory.Domain;

namespace OrderSaga.Orchestration.CoordinatorHost;

/// <summary>
/// Status uses the same vocabulary Choreography's GET /orders/{id} reports (derived from
/// Inventory event history via <see cref="OrderStatusProjection"/>), so the two stacks agree on
/// what the same real-world state is called. SagaStep is orchestration-only richer detail --
/// Choreography has no equivalent, since it has no central saga state at all.
/// </summary>
public sealed record OrderDetails(string OrderId, string Sku, int Quantity, decimal Amount, string? Status, IReadOnlyList<OrderHistoryEntry> History, string SagaStep);
