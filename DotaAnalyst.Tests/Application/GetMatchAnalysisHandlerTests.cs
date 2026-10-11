using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Tests.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Application;

public class GetMatchAnalysisHandlerTests
{
    private static readonly AccountId Me = MatchPlayerBuilder.Account(99517715);
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

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
        var handler = CreateHandler(new FakeReplayAnalyzer(null) { Fail = true });

        await Assert.ThrowsAsync<DataSourceUnavailableException>(
            () => handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None));
    }

    [Theory]
    [InlineData(true, 24 * 30)] // parsed: final, however old
    [InlineData(false, 0)] // unparsed, but saved moments ago
    public async Task HandleAsync_UsableSavedMatch_DoesNotAskOpenDota(bool isParsed, int savedHoursAgo)
    {
        var saved = new SavedMatch(CreateMatch(Team.Radiant, isParsed), Now.AddHours(-savedHoursAgo));
        var analyzer = new FakeReplayAnalyzer(null) { Fail = true };
        var handler = CreateHandler(analyzer, new FakeMatchRepository(saved));

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.NotNull(analysis);
        Assert.Equal(0, analyzer.Calls);
    }

    [Fact]
    public async Task HandleAsync_OldUnparsedSavedMatch_IsRefreshedFromOpenDota()
    {
        var saved = new SavedMatch(CreateMatch(Team.Radiant, isParsed: false), Now - GetMatchAnalysisHandler.UnparsedRefreshInterval);
        var fresh = CreateMatch(Team.Radiant, isParsed: true);
        var repository = new FakeMatchRepository(saved);
        var handler = CreateHandler(new FakeReplayAnalyzer(fresh), repository);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.True(analysis!.IsParsed);
        Assert.Same(fresh, Assert.Single(repository.Saved));
    }

    [Fact]
    public async Task HandleAsync_OldUnparsedSavedMatchAndOpenDotaDown_UsesSavedMatch()
    {
        var saved = new SavedMatch(CreateMatch(Team.Radiant, isParsed: false), Now.AddDays(-1));
        var handler = CreateHandler(new FakeReplayAnalyzer(null) { Fail = true }, new FakeMatchRepository(saved));

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.NotNull(analysis);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_FinishedMatchFromOpenDota_IsSaved(bool isParsed)
    {
        var match = CreateMatch(Team.Radiant, isParsed);
        var repository = new FakeMatchRepository();
        var handler = CreateHandler(new FakeReplayAnalyzer(match), repository);

        await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Same(match, Assert.Single(repository.Saved));
    }

    [Fact]
    public async Task HandleAsync_MatchWithUnknownResult_IsNotSaved()
    {
        var repository = new FakeMatchRepository();
        var handler = CreateHandler(new FakeReplayAnalyzer(CreateMatch(winningTeam: null, isParsed: false)), repository);

        await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Empty(repository.Saved);
    }

    [Fact]
    public async Task HandleAsync_DatabaseFails_StillAnswersFromOpenDota()
    {
        var handler = CreateHandler(
            new FakeReplayAnalyzer(CreateMatch(Team.Radiant, isParsed: true)),
            new FakeMatchRepository { Fail = true });

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.NotNull(analysis);
    }

    private static GetMatchAnalysisHandler CreateHandler(MatchDetails? match) =>
        CreateHandler(new FakeReplayAnalyzer(match));

    private static GetMatchAnalysisHandler CreateHandler(FakeReplayAnalyzer analyzer, FakeMatchRepository? repository = null) =>
        new(
            analyzer,
            repository ?? new FakeMatchRepository(),
            new FakeHeroCatalog(new Hero(31, "Lich")),
            new FakeItemCatalog(new Item(1, "Blink Dagger"), new Item(2, "Trusty Shovel")),
            new FixedTimeProvider(Now),
            NullLogger<GetMatchAnalysisHandler>.Instance);

    private static MatchDetails CreateMatch(Team? winningTeam, params MatchPlayer[] players) =>
        CreateMatch(winningTeam, isParsed: false, players);

    private static MatchDetails CreateMatch(Team? winningTeam, bool isParsed, params MatchPlayer[] players) =>
        new(MatchIdOf(10), TimeSpan.FromSeconds(1725), DateTimeOffset.UnixEpoch, winningTeam, 35, 20, isParsed, players);

    private static MatchId MatchIdOf(long value)
    {
        MatchId.TryCreate(value, out var matchId);
        return matchId;
    }

    private class FakeReplayAnalyzer(MatchDetails? match) : IReplayAnalyzer
    {
        public bool Fail { get; init; }

        public int Calls { get; private set; }

        public Task<MatchDetails?> GetMatchDetailsAsync(MatchId matchId, CancellationToken cancellationToken)
        {
            Calls++;
            return Fail ? throw new DataSourceUnavailableException("OpenDota is down.") : Task.FromResult(match);
        }
    }

    private class FakeMatchRepository(SavedMatch? saved = null) : IMatchRepository
    {
        public List<MatchDetails> Saved { get; } = [];

        public bool Fail { get; init; }

        public Task<SavedMatch?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Fail ? throw new StorageUnavailableException("The database is down.") : Task.FromResult(saved);

        public Task SaveAsync(MatchDetails match, DateTimeOffset savedAt, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new StorageUnavailableException("The database is down.");
            }

            Saved.Add(match);
            return Task.CompletedTask;
        }
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
