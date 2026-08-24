namespace OrderSaga.Choreography.Host.Tests;

public class InMemoryIdempotencyStoreTests
{
    [Fact]
    public void TryClaim_FirstTime_ReturnsTrue()
    {
        var store = new InMemoryIdempotencyStore();

        var claimed = store.TryClaim("MESSAGE-1");

        Assert.True(claimed);
    }

    [Fact]
    public void TryClaim_DuplicateMessageId_ReturnsFalse()
    {
        var store = new InMemoryIdempotencyStore();
        store.TryClaim("MESSAGE-1");

        var claimed = store.TryClaim("MESSAGE-1");

        Assert.False(claimed);
    }
}
