using OrderSaga.Shared;

namespace OrderSaga.Orchestration;

public sealed class PaymentResponder
{
    private readonly EventBus _bus;
    private readonly decimal _threshold;

    public PaymentResponder(EventBus bus, decimal threshold)
    {
        _bus = bus;
        _threshold = threshold;
        _bus.Subscribe<ChargePaymentCommand>(OnChargePaymentCommand);
    }

    private void OnChargePaymentCommand(ChargePaymentCommand command)
    {
        if (command.Amount > _threshold)
        {
            _bus.Publish(new PaymentDeclinedReply(command.OrderId, command.Sku));
        }
        else
        {
            _bus.Publish(new PaymentChargedReply(command.OrderId, command.Sku));
        }
    }
}
