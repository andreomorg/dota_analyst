using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.OpenDota;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class OpenDotaParseRequestRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ConcurrentClaims_OneOwner_ExpiredOwnerCannotWrite()
    {
        await fixture.ResetAsync();
        var repo = new OpenDotaParseRequestRepository(fixture.DbContextFactory, new StorageGuard(TimeProvider.System));
        var now = DateTimeOffset.UtcNow;
        await Task.WhenAll(repo.EnsureAsync(10001, now, default), repo.EnsureAsync(10001, now, default));
        var claims = await Task.WhenAll(repo.ClaimAsync(now, TimeSpan.FromMinutes(1), default),
            repo.ClaimAsync(now, TimeSpan.FromMinutes(1), default));
        var first = Assert.Single(claims.OfType<DotaAnalyst.Application.Abstractions.OpenDotaParseRequest>());
        var resumed = await repo.ClaimAsync(now.AddMinutes(2), TimeSpan.FromMinutes(1), default);
        Assert.NotNull(resumed);
        Assert.NotEqual(first.LeaseToken, resumed.LeaseToken);
        Assert.False(await repo.CompleteAsync(first with { State = "ready" }, default));
        Assert.True(await repo.CompleteAsync(resumed with { State = "processing", JobId = "123" }, default));
        Assert.Equal("123", (await repo.EnsureAsync(10001, now, default)).JobId);
    }

    [Fact]
    public async Task ReadyTask_IsNotClaimedAgain()
    {
        await fixture.ResetAsync();
        var repo = new OpenDotaParseRequestRepository(fixture.DbContextFactory, new StorageGuard(TimeProvider.System));
        var now = DateTimeOffset.UtcNow;
        await repo.EnsureAsync(10002, now, default);
        var task = await repo.ClaimAsync(now, TimeSpan.FromMinutes(1), default);
        Assert.True(await repo.CompleteAsync(task! with { State = "ready" }, default));
        Assert.Null(await repo.ClaimAsync(now.AddDays(1), TimeSpan.FromMinutes(1), default));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("unavailable")]
    [InlineData("ready")]
    public async Task Enrichment_ReopensTerminalTaskAfterCooldown_WithoutParseIntent(string state)
    {
        await fixture.ResetAsync();
        var repo = new OpenDotaParseRequestRepository(fixture.DbContextFactory, new StorageGuard(TimeProvider.System));
        var now = DateTimeOffset.UtcNow;
        await repo.EnsureAsync(10003, now, default);
        var request = await repo.ClaimAsync(now, TimeSpan.FromMinutes(1), default);
        await repo.CompleteAsync(request! with { State = state }, default);
        Assert.Equal(state, (await repo.EnsureAsync(10003, now.AddMinutes(5), default, enrichmentOnly: true)).State);
        var reopened = await repo.EnsureAsync(10003, now.AddMinutes(16), default, enrichmentOnly: true);
        Assert.Equal("refreshing", reopened.State);
        Assert.True(reopened.EnrichmentOnly);
    }
}
