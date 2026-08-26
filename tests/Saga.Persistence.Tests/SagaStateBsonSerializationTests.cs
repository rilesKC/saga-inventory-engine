using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using OrderSaga.Orchestration;

namespace Saga.Persistence.Tests;

public class SagaStateBsonSerializationTests
{
    [Fact]
    public void Deserialize_ViaBsonSerializerDirectly_IgnoresMongoGeneratedId()
    {
        // MongoSagaStateStore stores SagaState as a flat top-level document (state.ToBsonDocument()),
        // not nested under a Payload key the way MongoInventoryEventStore's events are. Once such a
        // document round-trips through a real collection, Mongo has stamped an _id onto it that
        // SagaState has no property for -- deserializing it unmodified throws FormatException unless
        // something tells the Bson serializer to tolerate unmapped elements.
        //
        // MongoSagaStateStore's static constructor registers a BsonClassMap for SagaState with
        // SetIgnoreExtraElements(true) -- forcing it to run here (without constructing the store
        // itself, which needs a real IMongoDatabase this sandbox can't reach) proves the tolerance is
        // a property of SagaState's own Bson mapping process-wide, not something only
        // MongoSagaStateStore's own Deserialize method remembers to do. A hypothetical future read
        // path added to this store (a bulk read, a change-stream projection) that calls
        // BsonSerializer.Deserialize<SagaState> directly, bypassing this store's own code entirely
        // exactly as done here, is covered automatically.
        RuntimeHelpers.RunClassConstructor(typeof(MongoSagaStateStore).TypeHandle);

        var document = new SagaState("ORDER-1", "SKU-1", 4, 199.99m, SagaStep.AwaitingPayment, Version: 1).ToBsonDocument();
        document.InsertAt(0, new BsonElement("_id", ObjectId.GenerateNewId()));

        var state = BsonSerializer.Deserialize<SagaState>(document);

        Assert.Equal("ORDER-1", state.OrderId);
        Assert.Equal(SagaStep.AwaitingPayment, state.Step);
    }
}
