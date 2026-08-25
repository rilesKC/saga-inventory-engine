namespace OrderSaga.Aws.Tests;

public class InMemoryIdempotencyStoreTests
{
    [Fact]
    public async Task TryClaimAsync_FirstTime_ReturnsTrue()
    {
        var store = new InMemoryIdempotencyStore();

        var claimed = await store.TryClaimAsync("MESSAGE-1", CancellationToken.None);

        Assert.True(claimed);
    }

    [Fact]
    public async Task TryClaimAsync_DuplicateMessageId_ReturnsFalse()
    {
        var store = new InMemoryIdempotencyStore();
        await store.TryClaimAsync("MESSAGE-1", CancellationToken.None);

        var claimed = await store.TryClaimAsync("MESSAGE-1", CancellationToken.None);

        Assert.False(claimed);
    }

    [Fact]
    public async Task ReleaseAsync_ClaimedMessageId_AllowsReClaim()
    {
        var store = new InMemoryIdempotencyStore();
        await store.TryClaimAsync("MESSAGE-1", CancellationToken.None);

        await store.ReleaseAsync("MESSAGE-1", CancellationToken.None);

        Assert.True(await store.TryClaimAsync("MESSAGE-1", CancellationToken.None));
    }
}
