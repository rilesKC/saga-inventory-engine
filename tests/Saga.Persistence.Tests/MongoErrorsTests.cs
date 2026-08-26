using MongoDB.Driver;

namespace Saga.Persistence.Tests;

public class MongoErrorsTests
{
    [Fact]
    public void IsDuplicateKey_CategoryIsDuplicateKey_ReturnsTrue()
    {
        Assert.True(MongoErrors.IsDuplicateKey(ServerErrorCategory.DuplicateKey, code: 2));
    }

    [Fact]
    public void IsDuplicateKey_CodeIs11000_ReturnsTrue()
    {
        Assert.True(MongoErrors.IsDuplicateKey(ServerErrorCategory.Uncategorized, code: 11000));
    }

    [Fact]
    public void IsDuplicateKey_NeitherCategoryNorCode_ReturnsFalse()
    {
        Assert.False(MongoErrors.IsDuplicateKey(ServerErrorCategory.Uncategorized, code: 2));
    }

    [Fact]
    public void IsDuplicateKey_NullCategoryAndNullCode_ReturnsFalse()
    {
        Assert.False(MongoErrors.IsDuplicateKey(category: null, code: null));
    }
}
