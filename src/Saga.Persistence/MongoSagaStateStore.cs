using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using OrderSaga.Orchestration;

namespace Saga.Persistence;

/// <summary>
/// Upserts the current SagaState document, keyed by OrderId -- unlike
/// <see cref="MongoInventoryEventStore"/>, this isn't an append-only log: SagaState is a
/// coordinator's current position in a flow, not an independent record of facts, so overwriting
/// the previous document on every transition is the correct model here (see the Saga Persistence
/// spec's reasoning for why SagaState is snapshotted rather than event-sourced). A unique OrderId
/// index plus a {OrderId, Version} filter on the replace turns SaveAsync's expectedVersion check
/// into a real optimistic-concurrency guarantee, not just an in-process one -- two Hosts racing to
/// save the same order at the same expected version produce a duplicate-key error on whichever
/// loses, translated to ConcurrencyConflictException. Real behavior verified against the real Atlas
/// cluster, not LocalStack (MongoDB Atlas isn't an AWS service) -- same precedent as this project's
/// DynamoDB-backed stores.
/// </summary>
public sealed class MongoSagaStateStore : ISagaStateStore
{
    private const int DuplicateKeyErrorCode = 11000;

    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoSagaStateStore(IMongoDatabase database, string collectionName)
    {
        _collection = database.GetCollection<BsonDocument>(collectionName);
        _collection.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending(nameof(SagaState.OrderId)),
            new CreateIndexOptions { Unique = true }));
    }

    public async Task SaveAsync(SagaState state, int expectedVersion, CancellationToken cancellationToken)
    {
        var document = state.ToBsonDocument();
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq(nameof(SagaState.OrderId), state.OrderId),
            Builders<BsonDocument>.Filter.Eq(nameof(SagaState.Version), expectedVersion));

        try
        {
            await _collection.ReplaceOneAsync(filter, document, new ReplaceOptions { IsUpsert = true }, cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey || ex.WriteError?.Code == DuplicateKeyErrorCode)
        {
            var actual = await TryLoadAsync(state.OrderId, cancellationToken);
            throw new ConcurrencyConflictException(state.OrderId, expectedVersion, actual?.Version ?? 0);
        }
    }

    public async Task<SagaState?> TryLoadAsync(string orderId, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(nameof(SagaState.OrderId), orderId);
        var document = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Deserialize(document);
    }

    public async Task<IReadOnlyList<SagaState>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var documents = await _collection.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(cancellationToken);

        return documents
            .Select(Deserialize)
            .ToList();
    }

    // A document read back from the collection carries Mongo's own auto-generated _id, which
    // SagaState has no property for -- deserializing it unmodified throws FormatException. Stripped
    // here rather than adding a MongoDB.Bson attribute to SagaState itself, since OrderSaga.Orchestration
    // has no dependency on MongoDB.Bson (see MongoInventoryEventStore's own Payload-nesting for the
    // same reasoning applied to Inventory.Domain's events).
    private static SagaState Deserialize(BsonDocument document)
    {
        document.Remove("_id");
        return BsonSerializer.Deserialize<SagaState>(document);
    }
}
