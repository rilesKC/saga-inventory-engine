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
    /// Every known message type name -- what OutboundMessageForwarder subscribes to, and the set
    /// Terraform's SQS queues must be able to carry (checked against each other for choreography's
    /// equivalent; see EventTypeRegistryTerraformSyncTests for the pattern this could mirror if
    /// orchestration's Terraform ever needs the same guard).
    /// </summary>
    public static IReadOnlyCollection<string> KnownMessageTypeNames => TypesByName.Keys;

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
