using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Tests.Application;

// The use case is tested with fakes instead of OpenDota: the interfaces make this possible.
public class GetRecentMatchesHandlerTests
{
    private static readonly AccountId Account = CreateAccountId(87278757);

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
        var handler = new GetRecentMatchesHandler(
            new FakeMatchProvider(match),
            new FakeHeroCatalog(new Hero(75, "Silencer")));

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
        var handler = new GetRecentMatchesHandler(new FakeMatchProvider(match), new FakeHeroCatalog());

        var result = await handler.HandleAsync(Account, CancellationToken.None);

        Assert.Equal(GetRecentMatchesHandler.UnknownHeroName, Assert.Single(result).HeroName);
    }

    private static AccountId CreateAccountId(long id)
    {
        AccountId.TryCreate(id, out var accountId);
        return accountId;
    }

    private class FakeMatchProvider(params PlayerMatch[] matches) : IMatchProvider
    {
        public Task<IReadOnlyList<PlayerMatch>> GetRecentMatchesAsync(
            AccountId accountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlayerMatch>>(matches);
    }

    private class FakeHeroCatalog(params Hero[] heroes) : IHeroCatalog
    {
        public Task<IReadOnlyDictionary<int, Hero>> GetHeroesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, Hero>>(heroes.ToDictionary(hero => hero.Id));
    }
}
