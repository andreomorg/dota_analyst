using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Stats;
using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Baseline;
using DotaAnalyst.Tests.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public class BaselineRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static HeroMetrics Hero(int heroId, int role, int deaths, Team team = Team.Radiant) =>
        new(heroId, team, role, deaths, 1, 0, 0.2, 0.05, 3000, 7000, 40, 0.25, 0, 0, null, 0, 0,
            PerRegion<double>.Of(_ => 0.125), PerRegion<int>.Of(region => region == MapRegion.River ? 1 : 0),
            RoleInferred: true, FirstBigItemSeconds: 900);

    // A 12:30 match: points at 0:00, 10:00 and the end.
    private static HeroStatHistory History(int lastHits) => new(
        [0, 600, 750],
        new Dictionary<StatKey, IReadOnlyList<int>> { [StatKey.LastHits] = [0, lastHits, lastHits + 5] });

    [Fact]
    public async Task SaveThenRead_KeepsTheStatisticsPerMinute()
    {
        var repository = CreateRepository();
        var match = Collected(1, BaselineSource.Pro, Now.AddDays(-1), Hero(31, 5, 3), Hero(1, 1, 2)) with
        {
            Histories = new Dictionary<int, HeroStatHistory> { [31] = History(lastHits: 40) },
        };
        await repository.SaveAsync(match, CancellationToken.None);

        var lich = Assert.Single(await repository.GetSamplesAsync(31, 5, BaselineSource.Pro, Now.AddDays(-60), CancellationToken.None));
        var other = Assert.Single(await repository.GetSamplesAsync(1, 1, BaselineSource.Pro, Now.AddDays(-60), CancellationToken.None));

        Assert.Equal(40, lich.History!.ValueAt(StatKey.LastHits, 600));
        Assert.Null(other.History);
    }

    private static BaselineMatch Collected(long matchId, BaselineSource source, DateTimeOffset start, params HeroMetrics[] heroes) =>
        new(ReplayBuilder.MatchIdOf(matchId), source, start, 2100, BaselineMatchStatus.Collected, heroes, Now);

    [Fact]
    public async Task SaveThenRead_SamplesByHeroRoleSourceAndTime()
    {
        var repository = CreateRepository();
        await repository.SaveAsync(Collected(1, BaselineSource.Pro, Now.AddDays(-1), Hero(31, 5, 3), Hero(1, 1, 2)), CancellationToken.None);
        await repository.SaveAsync(Collected(2, BaselineSource.HighRank, Now.AddDays(-1), Hero(31, 5, 4)), CancellationToken.None);
        await repository.SaveAsync(Collected(3, BaselineSource.Pro, Now.AddDays(-90), Hero(31, 5, 9)), CancellationToken.None);

        var samples = await repository.GetSamplesAsync(31, 5, BaselineSource.Pro, Now.AddDays(-60), CancellationToken.None);

        var sample = Assert.Single(samples);
        Assert.Equal(Hero(31, 5, 3), sample.Metrics);
        Assert.Equal(2100, sample.MatchSeconds);
    }

    [Fact]
    public async Task SaveThenRead_LaneMatchupSamplesByBothHeroesAndRole()
    {
        var repository = CreateRepository();
        await repository.SaveAsync(
            Collected(1, BaselineSource.Pro, Now.AddDays(-1),
                Hero(31, 5, 3), Hero(44, 4, 2, Team.Dire)),
            CancellationToken.None);

        var groups = await repository.GetMatchupGroupsAsync(Now.AddDays(-60), CancellationToken.None);
        var sample = Assert.Single(await repository.GetMatchupSamplesAsync(
            31, 5, 44, BaselineSource.Pro, Now.AddDays(-60), CancellationToken.None));
        var generalSamples = await repository.GetSamplesAsync(31, 5, BaselineSource.Pro, Now.AddDays(-60), CancellationToken.None);

        Assert.Contains((31, 5, 44, BaselineSource.Pro), groups);
        Assert.Equal((31, 2100), (sample.Metrics.HeroId, sample.MatchSeconds));
        Assert.Equal(44, sample.OpponentHeroId);
        Assert.Single(generalSamples);
    }

    [Fact]
    public async Task KnownMatches_AndTheDailyCount_OnlyCountCollected()
    {
        var repository = CreateRepository();
        await repository.SaveAsync(Collected(1, BaselineSource.Pro, Now, Hero(31, 5, 3)), CancellationToken.None);
        await repository.SaveAsync(
            new BaselineMatch(ReplayBuilder.MatchIdOf(2), BaselineSource.Pro, Now, 0, BaselineMatchStatus.NoReplay, [], Now),
            CancellationToken.None);

        var known = await repository.FindKnownAsync([ReplayBuilder.MatchIdOf(1), ReplayBuilder.MatchIdOf(2), ReplayBuilder.MatchIdOf(3)], CancellationToken.None);

        Assert.Equal([1L, 2L], known.Order());
        Assert.Equal(1, await repository.CountCollectedSinceAsync(Now.AddDays(-1), CancellationToken.None));
    }

    [Fact]
    public async Task SavingAgain_ReplacesTheEarlierTry()
    {
        var repository = CreateRepository();
        await repository.SaveAsync(Collected(1, BaselineSource.Pro, Now, Hero(31, 5, 3)), CancellationToken.None);
        await repository.SaveAsync(Collected(1, BaselineSource.Pro, Now, Hero(31, 5, 6)), CancellationToken.None);

        var sample = Assert.Single(await repository.GetSamplesAsync(31, 5, BaselineSource.Pro, Now.AddDays(-1), CancellationToken.None));
        Assert.Equal(6, sample.Metrics.Deaths);
    }

    [Fact]
    public async Task Groups_ListEveryHeroRoleAndSourceSinceThen()
    {
        var repository = CreateRepository();
        await repository.SaveAsync(Collected(1, BaselineSource.Pro, Now.AddDays(-1), Hero(31, 5, 3), Hero(1, 1, 2)), CancellationToken.None);
        await repository.SaveAsync(Collected(2, BaselineSource.HighRank, Now.AddDays(-1), Hero(31, 5, 4)), CancellationToken.None);
        await repository.SaveAsync(Collected(3, BaselineSource.Pro, Now.AddDays(-90), Hero(9, 4, 1)), CancellationToken.None);

        var groups = await repository.GetGroupsAsync(Now.AddDays(-60), CancellationToken.None);

        Assert.Equal(
            [(1, 1, BaselineSource.Pro), (31, 5, BaselineSource.Pro), (31, 5, BaselineSource.HighRank)],
            groups.OrderBy(group => group.HeroId).ThenBy(group => group.Source));
    }

    [Fact]
    public async Task CountByHero_OnlyThatSourceSinceThen()
    {
        var repository = CreateRepository();
        await repository.SaveAsync(Collected(1, BaselineSource.HighRank, Now.AddDays(-1), Hero(31, 5, 3), Hero(1, 1, 2)), CancellationToken.None);
        await repository.SaveAsync(Collected(2, BaselineSource.HighRank, Now.AddDays(-2), Hero(31, 4, 4)), CancellationToken.None);
        await repository.SaveAsync(Collected(3, BaselineSource.Pro, Now.AddDays(-1), Hero(31, 5, 1)), CancellationToken.None);
        await repository.SaveAsync(Collected(4, BaselineSource.HighRank, Now.AddDays(-90), Hero(31, 5, 1)), CancellationToken.None);

        var counts = await repository.CountMatchesByHeroAsync(BaselineSource.HighRank, Now.AddDays(-60), CancellationToken.None);

        Assert.Equal(new Dictionary<int, int> { [31] = 2, [1] = 1 }, counts);
    }

    [Fact]
    public async Task FinishedBase_ReplaceAllThenFind()
    {
        var store = new BaselineDistributionStore(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));
        var distributions = BaselineComparer.Distribute(
            Enumerable.Range(1, 10).Select(i => new BaselineSample(Hero(31, 5, i), 1700)).ToList());
        var timed = BaselineComparer.DistributeTimed(Enumerable.Range(1, 10).Select(i => History(lastHits: 10 * i)).ToList());
        await store.ReplaceAllAsync([new(31, 5, BaselineSource.Pro, 99, [])], Now, CancellationToken.None);

        await store.ReplaceAllAsync([new(31, 5, BaselineSource.Pro, 10, distributions, Timed: timed)], Now, CancellationToken.None);

        var set = await store.FindAsync(31, 5, BaselineSource.Pro, CancellationToken.None);
        Assert.NotNull(set);
        Assert.Equal(10, set.Matches);
        // Kept with two decimals (enough to compare; the mirror stays small).
        Assert.Equal(
            distributions.Select(d => d with { Quantiles = d.Quantiles.Select(value => Math.Round(value, 2)).ToList() }),
            set.Distributions);
        Assert.Equal(timed.Select(d => d with { Quantiles = d.Quantiles.Select(value => Math.Round(value, 2)).ToList() }), set.Timed);
        Assert.Contains(set.Timed!, d => d is { Stat: StatKey.LastHits, Second: 600, SampleSize: 10 });
        await store.ReplaceAllAsync(
            [
                new(31, 5, BaselineSource.Pro, 10, distributions),
                new(31, 5, BaselineSource.Pro, 12, distributions, 44),
            ],
            Now, CancellationToken.None);
        var matchup = await store.FindMatchupAsync(31, 5, 44, BaselineSource.Pro, CancellationToken.None);
        Assert.NotNull(matchup);
        Assert.Equal(12, matchup.Matches);
        Assert.Equal(44, matchup.OpponentHeroId);
        Assert.Null(await store.FindAsync(31, 5, BaselineSource.HighRank, CancellationToken.None));
    }

    private BaselineRepository CreateRepository() =>
        new(postgres.DbContextFactory, new StorageGuard(TimeProvider.System), NullLogger<BaselineRepository>.Instance);
}
