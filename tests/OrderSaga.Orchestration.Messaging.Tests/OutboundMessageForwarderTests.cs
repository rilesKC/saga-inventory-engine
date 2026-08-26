using OrderSaga.Orchestration;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Messaging.Tests;

public class OutboundMessageForwarderTests
{
    private sealed class RecordingMessagePublisher : IMessagePublisher
    {
        public readonly List<object> Published = [];

        public void Publish(object message) => Published.Add(message);
    }

    [Fact]
    public void Publish_KnownType_ForwardsToTheSelectedPublisher()
    {
        var bus = new EventBus();
        var publisher = new RecordingMessagePublisher();
        _ = new OutboundMessageForwarder(bus, _ => publisher);
        var command = new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m);

        bus.Publish(command);

        var forwarded = Assert.Single(publisher.Published);
        Assert.Equal(command, forwarded);
    }

    [Fact]
    public void Publish_TwoTypesRoutedToDifferentPublishers_EachGoesToItsOwnPublisher()
    {
        var bus = new EventBus();
        var inventoryPublisher = new RecordingMessagePublisher();
        var statelessResponderPublisher = new RecordingMessagePublisher();
        _ = new OutboundMessageForwarder(bus, type =>
            type == typeof(ReserveStockCommand) ? inventoryPublisher : statelessResponderPublisher);
        var reserveStock = new ReserveStockCommand("ORDER-1", "SKU-1", 4, 199.99m);
        var chargePayment = new ChargePaymentCommand("ORDER-1", "SKU-1", 199.99m);

        bus.Publish(reserveStock);
        bus.Publish(chargePayment);

        Assert.Equal(reserveStock, Assert.Single(inventoryPublisher.Published));
        Assert.Equal(chargePayment, Assert.Single(statelessResponderPublisher.Published));
    }
}
