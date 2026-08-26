using Inventory.Domain;
using OrderSaga.Choreography;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Host;

/// <summary>
/// Constructs the three choreography participants against a real inbound/outbound EventBus pair.
/// Pulled out of Program.cs specifically so this composition -- the fix for a real production bug
/// (an unbounded republish loop from sharing one bus for both directions) -- is exercised by a unit
/// test instead of only ever being verified by running the whole host against LocalStack.
/// </summary>
public static class HostParticipantWiring
{
    public static void Wire(
        InboundEventBus inbound,
        OutboundEventBus outbound,
        decimal paymentDeclineThreshold,
        IInventoryEventStore eventStore)
    {
        _ = new InventoryParticipant(inbound, outbound, eventStore);
        _ = new PaymentStub(inbound, outbound, paymentDeclineThreshold);
        _ = new ShippingStub(inbound, outbound);
    }
}
