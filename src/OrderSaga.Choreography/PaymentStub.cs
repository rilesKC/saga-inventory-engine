using Inventory.Domain;

namespace OrderSaga.Choreography;

public sealed class PaymentStub
{
    private readonly EventBus _bus;
    private readonly decimal _threshold;
    private readonly Dictionary<string, decimal> _amountsByOrderId = [];

    public PaymentStub(EventBus bus, decimal threshold)
    {
        _bus = bus;
        _threshold = threshold;
        _bus.Subscribe<OrderPlaced>(OnOrderPlaced);
        _bus.Subscribe<StockReserved>(OnStockReserved);
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
            _bus.Publish(new PaymentDeclined(stockReserved.OrderId, stockReserved.Sku, amount));
        }
        else
        {
            _bus.Publish(new PaymentCharged(stockReserved.OrderId, stockReserved.Sku, amount));
        }
    }
}
