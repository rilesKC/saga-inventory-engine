using MongoDB.Driver;

namespace Saga.Persistence;

/// <summary>
/// Shared duplicate-key detection for MongoInventoryEventStore/MongoSagaStateStore -- both
/// translate a duplicate-key write error into ConcurrencyConflictException, but they call
/// different driver operations (InsertManyAsync vs. ReplaceOneAsync) that throw different
/// exception types with their own WriteError-shaped members, so the predicate is extracted here as
/// a pure function taking just the two relevant fields, rather than each store re-deriving its own
/// (previously inconsistent -- one checked Code only, the other Category-or-Code) detection logic.
/// </summary>
public static class MongoErrors
{
    public const int DuplicateKeyErrorCode = 11000;

    public static bool IsDuplicateKey(ServerErrorCategory? category, int? code) =>
        category == ServerErrorCategory.DuplicateKey || code == DuplicateKeyErrorCode;
}
