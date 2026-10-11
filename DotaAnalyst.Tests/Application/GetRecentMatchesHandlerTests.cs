using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Application;

// The use case is tested with fakes instead of OpenDota: the interfaces make this possible.
public class GetRecentMatchesHandlerTests
{
    private static readonly AccountId Account = CreateAccountId(87278757);
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ReturnsMatchesWithHeroNames()
    {
        // Arrange
        var match = new PlayerMatch(
            MatchId: 10,
            HeroId: 75,
            Team: Team.Dire,
            WinningTeam: Team.Dire,
            Kda: new Kda(4, 8, 10),
            Duration: TimeSpan.FromSeconds(1455),
            StartTime: DateTimeOffset.UnixEpoch);
        var handler = CreateHandler(new FakeMatchProvider(match), new FakeHeroCatalog(new Hero(75, "Silencer")));

        // Act
        var result = await handler.HandleAsync(Account, CancellationToken.None);

        // Assert
        var summary = Assert.Single(result);
        Assert.Equal(
            new MatchSummary(10, 75, "Silencer", 4, 8, 10, 1455, DateTimeOffset.UnixEpoch, IsRadiant: false, Won: true),
            summary);
    }

    [Fact]
    public async Task HandleAsync_UsesUnknownHeroNameWhenHeroIsMissing()
    {
        var match = new PlayerMatch(1, 999, Team.Radiant, null, new Kda(0, 0, 0), TimeSpan.Zero, DateTimeOffset.UnixEpoch);
        var handler = CreateHandler(new FakeMatchProvider(match));

        var result = await handler.HandleAsync(Account, CancellationToken.None);

        Assert.Equal(GetRecentMatchesHandler.UnknownHeroName, Assert.Single(result).HeroName);
    }

    [Fact]
    public async Task HandleAsync_RecordsSuccessfulSearch()
    {
        var searches = new FakePlayerSearchRepository();
        var handler = CreateHandler(new FakeMatchProvider(), searches: searches);

        await handler.HandleAsync(Account, CancellationToken.None);

        Assert.Equal([(Account, Now)], searches.Recorded);
    }

    [Fact]
    public async Task HandleAsync_DoesNotRecordFailedSearch()
    {
        var searches = new FakePlayerSearchRepository();
        var handler = CreateHandler(new FakeMatchProvider { Fail = true }, searches: searches);

        await Assert.ThrowsAsync<DataSourceUnavailableException>(() => handler.HandleAsync(Account, CancellationToken.None));

        Assert.Empty(searches.Recorded);
    }

    [Fact]
    public async Task HandleAsync_StillReturnsMatchesWhenDatabaseFails()
    {
        var match = new PlayerMatch(1, 75, Team.Radiant, null, new Kda(0, 0, 0), TimeSpan.Zero, DateTimeOffset.UnixEpoch);
        var handler = CreateHandler(new FakeMatchProvider(match), searches: new FakePlayerSearchRepository { Fail = true });

        var result = await handler.HandleAsync(Account, CancellationToken.None);

        Assert.Single(result);
    }

    private static GetRecentMatchesHandler CreateHandler(
        FakeMatchProvider matches, FakeHeroCatalog? heroes = null, FakePlayerSearchRepository? searches = null) =>
        new(
            matches,
            heroes ?? new FakeHeroCatalog(),
            searches ?? new FakePlayerSearchRepository(),
            new FixedTimeProvider(Now),
            NullLogger<GetRecentMatchesHandler>.Instance);

    private static AccountId CreateAccountId(long id)
    {
        AccountId.TryCreate(id, out var accountId);
        return accountId;
    }

    private class FakeMatchProvider(params PlayerMatch[] matches) : IMatchProvider
    {
        public bool Fail { get; init; }

        public Task<IReadOnlyList<PlayerMatch>> GetRecentMatchesAsync(
            AccountId accountId, CancellationToken cancellationToken) =>
            Fail
                ? throw new DataSourceUnavailableException("OpenDota is down.")
                : Task.FromResult<IReadOnlyList<PlayerMatch>>(matches);
    }

    private class FakeHeroCatalog(params Hero[] heroes) : IHeroCatalog
    {
        public Task<IReadOnlyDictionary<int, Hero>> GetHeroesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, Hero>>(heroes.ToDictionary(hero => hero.Id));
    }

    private class FakePlayerSearchRepository : IPlayerSearchRepository
    {
        public List<(AccountId, DateTimeOffset)> Recorded { get; } = [];

        public bool Fail { get; init; }

        public Task RecordSearchAsync(AccountId accountId, DateTimeOffset searchedAt, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new StorageUnavailableException("The database is down.");
            }

            Recorded.Add((accountId, searchedAt));
            return Task.CompletedTask;
        }
    }
}
