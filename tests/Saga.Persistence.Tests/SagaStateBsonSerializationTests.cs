using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using OrderSaga.Orchestration;

namespace Saga.Persistence.Tests;

public class SagaStateBsonSerializationTests
{
    [Fact]
    public void Deserialize_DocumentWithMongoGeneratedIdRemoved_DoesNotThrow()
    {
        // MongoSagaStateStore stores SagaState as a flat top-level document (state.ToBsonDocument()),
        // not nested under a Payload key the way MongoInventoryEventStore's events are. Once such a
        // document round-trips through a real collection, Mongo has stamped an _id onto it that
        // SagaState has no property for -- deserializing the raw document (as MongoSagaStateStore did
        // before this fix) throws FormatException. MongoSagaStateStore is expected to strip _id
        // before deserializing, exactly as reproduced here, rather than the domain type SagaState
        // itself taking on a MongoDB-specific attribute (OrderSaga.Orchestration has no dependency on
        // MongoDB.Bson, deliberately -- see MongoInventoryEventStore's own Payload-nesting for the
        // same reasoning applied to Inventory.Domain's events).
        var document = new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.AwaitingPayment, Version: 1).ToBsonDocument();
        document.InsertAt(0, new BsonElement("_id", ObjectId.GenerateNewId()));

        document.Remove("_id");
        var state = BsonSerializer.Deserialize<SagaState>(document);

        Assert.Equal("ORDER-1", state.OrderId);
        Assert.Equal(SagaStep.AwaitingPayment, state.Step);
    }
}
