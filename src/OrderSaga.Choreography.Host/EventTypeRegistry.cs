using System.Text.Json;
using Inventory.Domain;
using NewRelic.Api.Agent;
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

    /// <summary>
    /// Producer-side half of manual distributed tracing across the EventBridge/SQS hop -- the New
    /// Relic .NET agent auto-instruments HTTP calls and a handful of known client libraries, but
    /// has no idea about this project's own EventBus/SQS polling code. If there's an active
    /// transaction (an HTTP request via OrderIntakeHandler, or an SQS message already being
    /// processed under [Transaction] on SqsMessageProcessor), InsertDistributedTraceHeaders'
    /// setter populates carrier; with no active transaction (e.g. the outbox drainer's own timer-
    /// driven publish, or no agent attached at all) the setter is simply never invoked and carrier
    /// stays empty, collapsed to null here rather than shipping an empty-but-non-null TraceContext
    /// on every message.
    /// </summary>
    public static EventEnvelope Serialize(object @event)
    {
        var eventType = @event.GetType();
        var payload = JsonSerializer.SerializeToElement(@event, eventType);

        var carrier = new Dictionary<string, string>();
        NewRelic.Api.Agent.NewRelic.GetAgent().CurrentTransaction
            .InsertDistributedTraceHeaders(carrier, (c, key, value) => c[key] = value);

        return new EventEnvelope(Guid.NewGuid().ToString(), eventType.Name, payload, carrier.Count > 0 ? carrier : null);
    }

    /// <summary>
    /// Consumer-side half -- see Serialize's doc comment. AcceptDistributedTraceHeaders is a
    /// documented no-op when envelope.TraceContext is null/empty or no agent is attached, so this
    /// is safe to call unconditionally.
    /// </summary>
    public static object Deserialize(EventEnvelope envelope)
    {
        var type = TypesByName[envelope.EventType];
        var @event = envelope.Payload.Deserialize(type)
            ?? throw new InvalidOperationException($"Failed to deserialize event of type '{envelope.EventType}'.");

        if (envelope.TraceContext is { } traceContext)
        {
            NewRelic.Api.Agent.NewRelic.GetAgent().CurrentTransaction
                .AcceptDistributedTraceHeaders(traceContext, (c, key) => c.TryGetValue(key, out var value) ? [value] : [], TransportType.Queue);
        }

        return @event;
    }
}
