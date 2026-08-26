using Inventory.Domain;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Saga.Persistence;

/// <summary>
/// Appends each event as its own document (Sku, an explicit Sequence number, an EventType
/// discriminator, a real nested Payload document rather than a pre-serialized string) and replays
/// by querying back in Sequence order. A unique {Sku, Sequence} index turns
/// AppendRangeAsync's expectedEventCount check into a real optimistic-concurrency guarantee, not
/// just an in-process one -- two Hosts racing to append at the same expected sequence produce a
/// duplicate-key error on whichever loses, translated to ConcurrencyConflictException. No other
/// logic worth unit-testing in isolation; real append/replay/conflict behavior is verified against
/// the real Atlas cluster, not LocalStack (MongoDB Atlas isn't an AWS service) -- same precedent as
/// this project's DynamoDB-backed stores.
/// </summary>
public sealed class MongoInventoryEventStore : IInventoryEventStore
{
    private const int DuplicateKeyErrorCode = 11000;

    private static readonly Dictionary<string, Type> TypesByName = new()
    {
        [nameof(StockSeeded)] = typeof(StockSeeded),
        [nameof(StockReserved)] = typeof(StockReserved),
        [nameof(ReservationConfirmed)] = typeof(ReservationConfirmed),
        [nameof(ReservationReleased)] = typeof(ReservationReleased),
    };

    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoInventoryEventStore(IMongoDatabase database, string collectionName)
    {
        _collection = database.GetCollection<BsonDocument>(collectionName);

        var indexKeys = Builders<BsonDocument>.IndexKeys.Ascending("Sku").Ascending("Sequence");
        _collection.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(indexKeys, new CreateIndexOptions { Unique = true }));
    }

    public async Task AppendRangeAsync(string sku, int expectedEventCount, IReadOnlyList<object> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        var documents = events.Select((@event, i) => new BsonDocument
        {
            ["Sku"] = sku,
            ["Sequence"] = expectedEventCount + i,
            ["EventType"] = @event.GetType().Name,
            ["Payload"] = @event.ToBsonDocument(@event.GetType()),
        });

        try
        {
            await _collection.InsertManyAsync(documents, cancellationToken: cancellationToken);
        }
        catch (MongoBulkWriteException<BsonDocument> ex) when (ex.WriteErrors.Any(e => e.Code == DuplicateKeyErrorCode))
        {
            var actualEventCount = (int)await _collection.CountDocumentsAsync(
                Builders<BsonDocument>.Filter.Eq("Sku", sku), cancellationToken: cancellationToken);
            throw new ConcurrencyConflictException(sku, expectedEventCount, actualEventCount);
        }
    }

    public async Task<IReadOnlyList<object>> LoadEventsAsync(string sku, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("Sku", sku);
        var sort = Builders<BsonDocument>.Sort.Ascending("Sequence");

        var documents = await _collection.Find(filter).Sort(sort).ToListAsync(cancellationToken);

        return documents
            .Select(document =>
            {
                var eventType = TypesByName[document["EventType"].AsString];
                return BsonSerializer.Deserialize(document["Payload"].AsBsonDocument, eventType);
            })
            .ToList();
    }
}
