using OrderSaga.Orchestration;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.ResponderHost;

/// <summary>
/// Constructs PaymentResponder and ShippingResponder against a real inbound/outbound EventBus
/// pair. Same pattern as CoordinatorWiring -- see its doc comment.
/// </summary>
public static class ResponderWiring
{
    public static void Wire(InboundEventBus inbound, OutboundEventBus outbound, decimal paymentDeclineThreshold)
    {
        _ = new PaymentResponder(inbound, outbound, paymentDeclineThreshold);
        _ = new ShippingResponder(inbound, outbound);
    }
}
