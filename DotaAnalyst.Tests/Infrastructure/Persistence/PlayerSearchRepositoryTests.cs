using DotaAnalyst.Domain.Players;
using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Players;
using DotaAnalyst.Tests.Domain;
using Microsoft.EntityFrameworkCore;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public class PlayerSearchRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly AccountId Account = MatchPlayerBuilder.Account(99517715);
    private static readonly DateTimeOffset First = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FirstSearch_CreatesTheRow()
    {
        var repository = new PlayerSearchRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        await repository.RecordSearchAsync(Account, First, CancellationToken.None);

        var row = await LoadAsync();
        Assert.Equal(First, row.FirstSearchedAt);
        Assert.Equal(First, row.LastSearchedAt);
        Assert.Equal(1, row.SearchCount);
    }

    [Fact]
    public async Task LaterSearch_UpdatesLastSearchAndCount()
    {
        var repository = new PlayerSearchRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        await repository.RecordSearchAsync(Account, First, CancellationToken.None);
        await repository.RecordSearchAsync(Account, First.AddHours(1), CancellationToken.None);
        // An older request finishing last doesn't move last_searched_at back.
        await repository.RecordSearchAsync(Account, First.AddMinutes(30), CancellationToken.None);

        var row = await LoadAsync();
        Assert.Equal(First, row.FirstSearchedAt);
        Assert.Equal(First.AddHours(1), row.LastSearchedAt);
        Assert.Equal(3, row.SearchCount);
    }

    [Fact]
    public async Task SimultaneousSearches_AreAllCounted()
    {
        var repository = new PlayerSearchRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => repository.RecordSearchAsync(Account, First, CancellationToken.None)));

        Assert.Equal(20, (await LoadAsync()).SearchCount);
    }

    private async Task<PlayerSearchRecord> LoadAsync()
    {
        await using var db = await postgres.DbContextFactory.CreateDbContextAsync();
        return await db.PlayerSearches.SingleAsync(search => search.AccountId == Account.Value);
    }
}
