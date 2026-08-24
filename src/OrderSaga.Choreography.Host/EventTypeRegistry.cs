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

    public static EventEnvelope Serialize(object @event)
    {
        var eventType = @event.GetType();
        var payload = JsonSerializer.Serialize(@event, eventType);
        return new EventEnvelope(Guid.NewGuid().ToString(), eventType.Name, payload);
    }

    public static object Deserialize(EventEnvelope envelope)
    {
        var type = TypesByName[envelope.EventType];
        return JsonSerializer.Deserialize(envelope.Payload, type)
            ?? throw new InvalidOperationException($"Failed to deserialize event of type '{envelope.EventType}'.");
    }
}
