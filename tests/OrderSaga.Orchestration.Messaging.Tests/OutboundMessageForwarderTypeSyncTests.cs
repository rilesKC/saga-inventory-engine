using System.Runtime.CompilerServices;
using OrderSaga.Shared;

namespace OrderSaga.Orchestration.Messaging.Tests;

/// <summary>
/// OutboundMessageForwarder's constructor hardcodes one bus.Subscribe&lt;T&gt; call per message
/// type, and OrchestrationMessageTypeRegistry.KnownMessageTypes is a second, independently
/// maintained list of the same types (used for Serialize/Deserialize) -- add a type to the registry
/// and forget the forwarder, and that type's messages are silently never forwarded to SQS: no
/// exception, just a reply the coordinator waits on forever. This test is the guardrail: for every
/// type the registry knows about, it publishes an instance on the forwarder's bus and asserts the
/// forwarder actually picked it up, mirroring the drift guard choreography's
/// EventTypeRegistryTerraformSyncTests provides for OutboundEventForwarder (there checked against
/// Terraform instead, since orchestration's SQS queues aren't type-routed the way EventBridge rules
/// are).
/// </summary>
public class OutboundMessageForwarderTypeSyncTests
{
    private sealed class RecordingMessagePublisher : IMessagePublisher
    {
        public readonly List<object> Published = [];

        public void Publish(object message) => Published.Add(message);
    }

    [Fact]
    public void EveryKnownMessageType_IsSubscribedByTheForwarder()
    {
        var bus = new EventBus();
        var publisher = new RecordingMessagePublisher();
        _ = new OutboundMessageForwarder(bus, _ => publisher);

        foreach (var messageType in OrchestrationMessageTypeRegistry.KnownMessageTypes)
        {
            // Skips the constructor entirely -- only the runtime type matters for routing, not
            // valid field data, and these message records' constructors take arguments this test
            // has no principled way to invent generically for an arbitrary type.
            var instance = RuntimeHelpers.GetUninitializedObject(messageType);

            bus.Publish(instance);

            Assert.Contains(publisher.Published, p => ReferenceEquals(p, instance));
        }
    }
}
