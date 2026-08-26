using Inventory.Domain;
using OrderSaga.Orchestration;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.InventoryHost;

/// <summary>
/// Constructs the InventoryResponder against a real inbound/outbound EventBus pair. Same pattern
/// as CoordinatorWiring -- see its doc comment.
/// </summary>
public static class InventoryWiring
{
    public static void Wire(InboundEventBus inbound, OutboundEventBus outbound, IInventoryEventStore eventStore) =>
        _ = new InventoryResponder(inbound, outbound, eventStore);
}
