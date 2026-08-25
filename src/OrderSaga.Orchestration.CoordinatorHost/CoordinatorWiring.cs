using OrderSaga.Orchestration;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.CoordinatorHost;

/// <summary>
/// Constructs the SagaCoordinator against a real inbound/outbound EventBus pair. Pulled out of
/// Program.cs specifically so this composition is exercised by a unit test instead of only ever
/// being verified by running the whole host against LocalStack -- same pattern as choreography's
/// HostParticipantWiring, applied here from the start rather than retrofitted after a review.
/// </summary>
public static class CoordinatorWiring
{
    public static SagaCoordinator Wire(InboundEventBus inbound, OutboundEventBus outbound) =>
        new(inbound, outbound);
}
