using OrderSaga.Shared;

namespace OrderSaga.Orchestration;

public sealed class PaymentResponder
{
    private readonly InboundEventBus _inbound;
    private readonly OutboundEventBus _outbound;
    private readonly decimal _threshold;

    /// <param name="inbound">Subscribed to for trigger commands.</param>
    /// <param name="outbound">Published to for produced replies. See
    /// <see cref="SagaCoordinator"/>'s constructor for why these are separate.</param>
    public PaymentResponder(InboundEventBus inbound, OutboundEventBus outbound, decimal threshold)
    {
        _inbound = inbound;
        _outbound = outbound;
        _threshold = threshold;
        _inbound.Subscribe<ChargePaymentCommand>(OnChargePaymentCommand);
    }

    private void OnChargePaymentCommand(ChargePaymentCommand command)
    {
        if (command.Amount > _threshold)
        {
            _outbound.Publish(new PaymentDeclinedReply(command.OrderId, command.Sku));
        }
        else
        {
            _outbound.Publish(new PaymentChargedReply(command.OrderId, command.Sku));
        }
    }
}
