using System.Text.Json;
using Inventory.Domain;
using OrderSaga.Choreography;
using OrderSaga.Shared;

namespace OrderSaga.Choreography.Host;

public static class EventTypeRegistry
{
    private static readonly Dictionary<string, Type> TypesByName = new()
    {
        [nameof(OrderPlaced)] = typeof(OrderPlaced),
        [nameof(StockReserved)] = typeof(StockReserved),
        [nameof(StockReservationFailed)] = typeof(StockReservationFailed),
        [nameof(PaymentCharged)] = typeof(PaymentCharged),
        [nameof(PaymentDeclined)] = typeof(PaymentDeclined),
        [nameof(ReservationConfirmed)] = typeof(ReservationConfirmed),
        [nameof(ReservationReleased)] = typeof(ReservationReleased),
        [nameof(ShipmentScheduled)] = typeof(ShipmentScheduled),
    };

    /// <summary>
    /// The full set of event type names this registry knows how to (de)serialize -- also the set
    /// Terraform's EventBridge rules (infra/modules/messaging/eventbridge.tf) must route, checked
    /// against each other in EventTypeRegistryTerraformSyncTests.
    /// </summary>
    public static IReadOnlyCollection<string> KnownEventTypeNames => TypesByName.Keys;

    public static EventEnvelope Serialize(object @event)
    {
        var eventType = @event.GetType();
        var payload = JsonSerializer.SerializeToElement(@event, eventType);
        return new EventEnvelope(Guid.NewGuid().ToString(), eventType.Name, payload);
    }

    public static object Deserialize(EventEnvelope envelope)
    {
        var type = TypesByName[envelope.EventType];
        return envelope.Payload.Deserialize(type)
            ?? throw new InvalidOperationException($"Failed to deserialize event of type '{envelope.EventType}'.");
    }
}
