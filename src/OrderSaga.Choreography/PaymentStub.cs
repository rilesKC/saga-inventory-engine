using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Choreography;

public sealed class PaymentStub
{
    private readonly InboundEventBus _inbound;
    private readonly OutboundEventBus _outbound;
    private readonly decimal _threshold;

    /// <param name="inbound">Subscribed to for trigger events.</param>
    /// <param name="outbound">Published to for produced events. See
    /// <see cref="InventoryParticipant"/>'s constructor for why these are separate.</param>
    public PaymentStub(InboundEventBus inbound, OutboundEventBus outbound, decimal threshold)
    {
        _inbound = inbound;
        _outbound = outbound;
        _threshold = threshold;
        _inbound.Subscribe<StockReserved>(OnStockReserved);
    }

    private void OnStockReserved(StockReserved stockReserved)
    {
        var amount = stockReserved.Amount;

        if (amount > _threshold)
        {
            _outbound.Publish(new PaymentDeclined(stockReserved.OrderId, stockReserved.Sku, amount));
        }
        else
        {
            _outbound.Publish(new PaymentCharged(stockReserved.OrderId, stockReserved.Sku, amount));
        }
    }
}
