using System.Text.Json;
using OrderSaga.Orchestration;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Messaging;

public static class OrchestrationMessageTypeRegistry
{
    private static readonly Dictionary<string, Type> TypesByName = new()
    {
        [nameof(OrderPlaced)] = typeof(OrderPlaced),
        [nameof(ReserveStockCommand)] = typeof(ReserveStockCommand),
        [nameof(ConfirmReservationCommand)] = typeof(ConfirmReservationCommand),
        [nameof(ReleaseReservationCommand)] = typeof(ReleaseReservationCommand),
        [nameof(ChargePaymentCommand)] = typeof(ChargePaymentCommand),
        [nameof(ScheduleShipmentCommand)] = typeof(ScheduleShipmentCommand),
        [nameof(StockReservedReply)] = typeof(StockReservedReply),
        [nameof(StockReservationFailedReply)] = typeof(StockReservationFailedReply),
        [nameof(ReservationConfirmedReply)] = typeof(ReservationConfirmedReply),
        [nameof(ReservationReleasedReply)] = typeof(ReservationReleasedReply),
        [nameof(PaymentChargedReply)] = typeof(PaymentChargedReply),
        [nameof(PaymentDeclinedReply)] = typeof(PaymentDeclinedReply),
        [nameof(ShipmentScheduledReply)] = typeof(ShipmentScheduledReply),
    };

    /// <summary>
    /// Every known message type name -- what OutboundMessageForwarder's constructor subscribes to.
    /// Unlike choreography (whose equivalent list is checked against Terraform's EventBridge rules
    /// in EventTypeRegistryTerraformSyncTests), orchestration's SQS queues aren't type-routed, so
    /// there's no Terraform list to compare against here -- instead OutboundMessageForwarderTypeSyncTests
    /// checks this list against the forwarder's actual runtime subscriptions.
    /// </summary>
    public static IReadOnlyCollection<string> KnownMessageTypeNames => TypesByName.Keys;

    /// <summary>
    /// The same set as <see cref="KnownMessageTypeNames"/>, as runtime Types instead of names --
    /// exists so OutboundMessageForwarderTypeSyncTests can construct an instance of every known
    /// message type without hardcoding a second, independently-drifting type list of its own.
    /// </summary>
    public static IReadOnlyCollection<Type> KnownMessageTypes => TypesByName.Values;

    public static MessageEnvelope Serialize(object message)
    {
        var messageType = message.GetType();
        var payload = JsonSerializer.SerializeToElement(message, messageType);
        return new MessageEnvelope(Guid.NewGuid().ToString(), messageType.Name, payload);
    }

    public static object Deserialize(MessageEnvelope envelope)
    {
        var type = TypesByName[envelope.MessageType];
        return envelope.Payload.Deserialize(type)
            ?? throw new InvalidOperationException($"Failed to deserialize message of type '{envelope.MessageType}'.");
    }
}
