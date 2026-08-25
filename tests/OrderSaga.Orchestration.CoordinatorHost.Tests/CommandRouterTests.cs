using OrderSaga.Orchestration;
using OrderSaga.Orchestration.Messaging;

namespace OrderSaga.Orchestration.CoordinatorHost.Tests;

public class CommandRouterTests
{
    private sealed class RecordingMessagePublisher : IMessagePublisher
    {
        public void Publish(object message)
        {
        }
    }

    [Theory]
    [InlineData(typeof(ReserveStockCommand))]
    [InlineData(typeof(ConfirmReservationCommand))]
    [InlineData(typeof(ReleaseReservationCommand))]
    public void PublisherFor_ReserveStockOrConfirmOrReleaseCommand_ReturnsInventoryPublisher(Type commandType)
    {
        var inventoryPublisher = new RecordingMessagePublisher();
        var statelessResponderPublisher = new RecordingMessagePublisher();
        var router = new CommandRouter(inventoryPublisher, statelessResponderPublisher);

        var publisher = router.PublisherFor(commandType);

        Assert.Same(inventoryPublisher, publisher);
    }

    [Theory]
    [InlineData(typeof(ChargePaymentCommand))]
    [InlineData(typeof(ScheduleShipmentCommand))]
    public void PublisherFor_ChargePaymentOrScheduleShipmentCommand_ReturnsStatelessResponderPublisher(Type commandType)
    {
        var inventoryPublisher = new RecordingMessagePublisher();
        var statelessResponderPublisher = new RecordingMessagePublisher();
        var router = new CommandRouter(inventoryPublisher, statelessResponderPublisher);

        var publisher = router.PublisherFor(commandType);

        Assert.Same(statelessResponderPublisher, publisher);
    }
}
