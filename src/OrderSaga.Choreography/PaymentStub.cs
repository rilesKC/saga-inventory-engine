using Inventory.Domain;
using OrderSaga.Shared;

namespace OrderSaga.Choreography;

public sealed class PaymentStub
{
    private readonly EventBus _inbound;
    private readonly EventBus _outbound;
    private readonly decimal _threshold;
    private readonly Dictionary<string, decimal> _amountsByOrderId = [];

    /// <param name="inbound">Subscribed to for trigger events.</param>
    /// <param name="outbound">Published to for produced events. See
    /// <see cref="InventoryParticipant"/>'s constructor for why these are separate.</param>
    public PaymentStub(EventBus inbound, EventBus outbound, decimal threshold)
    {
        _inbound = inbound;
        _outbound = outbound;
        _threshold = threshold;
        _inbound.Subscribe<OrderPlaced>(OnOrderPlaced);
        _inbound.Subscribe<StockReserved>(OnStockReserved);
    }

    private void OnOrderPlaced(OrderPlaced orderPlaced)
    {
        _amountsByOrderId[orderPlaced.OrderId] = orderPlaced.Amount;
    }

    private void OnStockReserved(StockReserved stockReserved)
    {
        var amount = _amountsByOrderId[stockReserved.OrderId];

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
