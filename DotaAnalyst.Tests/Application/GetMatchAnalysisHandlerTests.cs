using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Tests.Domain;

namespace DotaAnalyst.Tests.Application;

public class GetMatchAnalysisHandlerTests
{
    private static readonly AccountId Me = MatchPlayerBuilder.Account(99517715);

    [Fact]
    public async Task HandleAsync_BuildsTeamsAndPerformanceForCurrentPlayer()
    {
        // Arrange
        var me = MatchPlayerBuilder.Create(
            accountId: Me,
            heroId: 31,
            team: Team.Radiant,
            itemIds: [1, 999],
            neutralItemId: 2,
            benchmarks:
            [
                new Benchmark(BenchmarkMetric.DeniesPerMinute, 0.63, 0.97),
                new Benchmark(BenchmarkMetric.KillsPerMinute, 0.07, 0.18),
            ]);
        var enemy = MatchPlayerBuilder.Create(accountId: null, heroId: 2, team: Team.Dire);
        var handler = CreateHandler(CreateMatch(Team.Radiant, me, enemy));

        // Act
        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        // Assert
        Assert.NotNull(analysis);
        Assert.True(analysis.RadiantWon);

        var mine = Assert.Single(analysis.Radiant);
        Assert.True(mine.IsCurrentPlayer);
        Assert.Equal("Lich", mine.HeroName);
        Assert.Equal(["Blink Dagger", GetMatchAnalysisHandler.UnknownItemName], mine.Items);
        Assert.Equal("Trusty Shovel", mine.NeutralItem);

        var theirs = Assert.Single(analysis.Dire);
        Assert.False(theirs.IsCurrentPlayer);
        Assert.Null(theirs.AccountId);
        Assert.Equal(GetMatchAnalysisHandler.UnknownHeroName, theirs.HeroName);

        var performance = analysis.Performance;
        Assert.NotNull(performance);
        Assert.Equal("Lich", performance.HeroName);
        Assert.True(performance.IsRadiant);
        Assert.True(performance.Won);
        Assert.Equal([BenchmarkMetric.DeniesPerMinute], performance.Strengths);
        Assert.Equal([BenchmarkMetric.KillsPerMinute], performance.Weaknesses);
        Assert.Contains(performance.Metrics, m => m.Metric == BenchmarkMetric.DeniesPerMinute && m.Rating == PerformanceRating.Strong);
    }

    [Fact]
    public async Task HandleAsync_ReturnsNullWhenMatchDoesNotExist()
    {
        var handler = CreateHandler(match: null);

        Assert.Null(await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_HasNoPerformanceWhenPlayerIsNotInTheMatch()
    {
        var someoneElse = MatchPlayerBuilder.Create(accountId: MatchPlayerBuilder.Account(1));
        var handler = CreateHandler(CreateMatch(Team.Dire, someoneElse));

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.NotNull(analysis);
        Assert.Null(analysis.Performance);
        Assert.False(analysis.RadiantWon);
        Assert.DoesNotContain(analysis.Radiant, p => p.IsCurrentPlayer);
    }

    [Fact]
    public async Task HandleAsync_PropagatesDataSourceFailure()
    {
        var handler = new GetMatchAnalysisHandler(new FailingReplayAnalyzer(), new FakeHeroCatalog(), new FakeItemCatalog());

        await Assert.ThrowsAsync<DataSourceUnavailableException>(
            () => handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None));
    }

    private static GetMatchAnalysisHandler CreateHandler(MatchDetails? match) =>
        new(
            new FakeReplayAnalyzer(match),
            new FakeHeroCatalog(new Hero(31, "Lich")),
            new FakeItemCatalog(new Item(1, "Blink Dagger"), new Item(2, "Trusty Shovel")));

    private static MatchDetails CreateMatch(Team? winningTeam, params MatchPlayer[] players) =>
        new(MatchIdOf(10), TimeSpan.FromSeconds(1725), DateTimeOffset.UnixEpoch, winningTeam, 35, 20, false, players);

    private static MatchId MatchIdOf(long value)
    {
        MatchId.TryCreate(value, out var matchId);
        return matchId;
    }

    private class FakeReplayAnalyzer(MatchDetails? match) : IReplayAnalyzer
    {
        public Task<MatchDetails?> GetMatchDetailsAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult(match);
    }

    private class FailingReplayAnalyzer : IReplayAnalyzer
    {
        public Task<MatchDetails?> GetMatchDetailsAsync(MatchId matchId, CancellationToken cancellationToken) =>
            throw new DataSourceUnavailableException("OpenDota is down.");
    }

    private class FakeHeroCatalog(params Hero[] heroes) : IHeroCatalog
    {
        public Task<IReadOnlyDictionary<int, Hero>> GetHeroesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, Hero>>(heroes.ToDictionary(hero => hero.Id));
    }

    private class FakeItemCatalog(params Item[] items) : IItemCatalog
    {
        public Task<IReadOnlyDictionary<int, Item>> GetItemsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, Item>>(items.ToDictionary(item => item.Id));
    }
}
