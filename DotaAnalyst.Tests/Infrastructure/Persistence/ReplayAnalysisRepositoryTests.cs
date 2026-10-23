using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Replays;
using DotaAnalyst.Tests.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public class ReplayAnalysisRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const long MatchIdValue = 9034320024;
    private static readonly MatchId Match = ReplayBuilder.MatchIdOf(MatchIdValue);
    private static readonly DateTimeOffset SavedAt = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SaveThenFind_Ready_KeepsTheWholeAnalysis()
    {
        var analysis = new ReplayAnalysis(Match, 540,
        [
            new LaningStats(31, Team.Radiant, Lane.Bottom, lastHits: 12, denies: 9, netWorth: 2047, experience: 2609, deaths: 2),
            new LaningStats(28, Team.Dire, Lane.Roaming, lastHits: 0, denies: 0, netWorth: 900, experience: 1000, deaths: 0),
        ]);
        var repository = CreateRepository();

        // Saved with a non-UTC offset: timestamptz only takes UTC, so the repository converts it.
        await repository.SaveAsync(SavedReplayAnalysis.Ready(analysis, SavedAt.ToOffset(TimeSpan.FromHours(-3))), CancellationToken.None);
        var found = await repository.FindAsync(Match, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(SavedReplayState.Ready, found.State);
        Assert.Equal(SavedAt, found.SavedAt);
        Assert.Equivalent(analysis, found.Analysis, strict: true);
    }

    [Fact]
    public async Task SaveThenFind_KeepsNeutralCraftsAndMadstones()
    {
        var analysis = new ReplayAnalysis(Match, 540,
            [new LaningStats(31, Team.Radiant, Lane.Bottom, lastHits: 12, denies: 9, netWorth: 2047, experience: 2609, deaths: 2)])
        {
            NeutralCrafts = [new NeutralCraft(31, 1207.5, "item_occult_bracelet", 1, 1, "item_enhancement_brawny", 1)],
            NeutralProgress = [new NeutralProgress(31, 17, 9, 3, 300)],
        };
        var repository = CreateRepository();

        await repository.SaveAsync(SavedReplayAnalysis.Ready(analysis, SavedAt), CancellationToken.None);
        var found = (await repository.FindAsync(Match, CancellationToken.None))!.Analysis!;

        Assert.Equal(analysis.NeutralCrafts, found.NeutralCrafts);
        Assert.Equal(analysis.NeutralProgress, found.NeutralProgress);
        // Rows saved before them read as unknown, not as "crafted nothing".
        var older = (await RoundTrip(new ReplayAnalysis(Match, 540,
            [new LaningStats(31, Team.Radiant, Lane.Bottom, lastHits: 0, denies: 0, netWorth: 0, experience: 0, deaths: 0)])))!;
        Assert.Null(older.NeutralCrafts);
        Assert.Null(older.NeutralProgress);

        async Task<ReplayAnalysis?> RoundTrip(ReplayAnalysis value)
        {
            await repository.SaveAsync(SavedReplayAnalysis.Ready(value, SavedAt), CancellationToken.None);
            return (await repository.FindAsync(Match, CancellationToken.None))?.Analysis;
        }
    }

    [Fact]
    public async Task SaveThenFind_ReadyWithTimeline_KeepsTheTimeline()
    {
        var analysis = ReplayBuilder.Analysis(
            MatchIdValue, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(28, Team.Dire, Lane.Top));
        var repository = CreateRepository();

        await repository.SaveAsync(SavedReplayAnalysis.Ready(analysis, SavedAt), CancellationToken.None);
        var found = await repository.FindAsync(Match, CancellationToken.None);

        Assert.NotNull(found?.Analysis?.Timeline);
        Assert.Equivalent(analysis.Timeline, found.Analysis.Timeline, strict: true);
    }

    [Fact]
    public async Task SaveThenFind_KeepsDamageAndAssists()
    {
        var players = new[] { ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(28, Team.Dire, Lane.Top) };
        var analysis = new ReplayAnalysis(
            Match, ReplayAnalysis.LaningPhaseEndsAtSeconds, players, ReplayBuilder.Timeline(players),
            deaths: [new HeroDeath(28, 300, 0, 0, KillerKind.Hero, 31, reincarnated: false, assistHeroIds: [7])],
            wards: [],
            damage:
            [
                new DamageRow(31, DamageRow.HeroKind, 28, DamageRow.HeroKind, "lich_frost_nova", 1200),
                new DamageRow(null, "buildings", 31, DamageRow.HeroKind, null, 300),
            ],
            purchases: [new ItemPurchase(31, -90, "tango"), new ItemPurchase(28, 1120, "recipe_black_king_bar")],
            objectives:
            [
                new MapObjective(ObjectiveKind.Tower, 680, Team.Dire, 28, Team.Radiant, towerTier: 2, lane: Lane.Middle),
                new MapObjective(ObjectiveKind.Barracks, 1668, Team.Dire, null, Team.Radiant, lane: Lane.Bottom, meleeBarracks: true),
                new MapObjective(ObjectiveKind.Roshan, 1300, Team.Radiant, 31, aegisHeroId: 31),
            ],
            metrics:
            [
                new HeroMetrics(31, Team.Radiant, 5, 4, 2, 1, 0.31, 0.12, 2900, null, 12, 0.18, 9, 6, 287, 3, 4,
                    new PerRegion<double>(0.1, 0, 0.3, 0.1, 0, 0.2, 0.2, 0.1), new PerRegion<int>(0, 1, 0, 0, 0, 1, 0, 2),
                    TimeByRegionByPhase: new PerGamePhase<PerRegion<double>>(
                        new PerRegion<double>(0.1, 0, 0.3, 0.1, 0, 0.2, 0.2, 0.1),
                        new PerRegion<double>(0.2, 0, 0.2, 0.1, 0, 0.2, 0.2, 0.1),
                        new PerRegion<double>(0.3, 0, 0.1, 0.1, 0, 0.2, 0.2, 0.1)),
                    DeathsByRegionByPhase: new PerGamePhase<PerRegion<int>>(
                        new PerRegion<int>(0, 1, 0, 0, 0, 1, 0, 2),
                        new PerRegion<int>(1, 0, 0, 0, 0, 1, 0, 1),
                        new PerRegion<int>(0, 0, 0, 1, 0, 1, 0, 0)),
                    FirstBigItemSeconds: 900),
            ],
            heatmaps:
            [
                new HeroHeatmap(31, new PerGamePhase<IReadOnlyList<HeatCell>>(
                    [new HeatCell(3, 28, 140), new HeatCell(4, 28, 60)], [new HeatCell(16, 16, 2)], []),
                    new PerGamePhase<TeamProximity>(new TeamProximity(120, 40), new TeamProximity(2, 1), default)),
            ],
            abilityUpgrades: [new AbilityUpgrade(31, 60, "lich_frost_nova", 1), new AbilityUpgrade(31, 300, "lich_chain_frost", 2)]);
        var repository = CreateRepository();

        await repository.SaveAsync(SavedReplayAnalysis.Ready(analysis, SavedAt), CancellationToken.None);
        var found = await repository.FindAsync(Match, CancellationToken.None);

        Assert.Equal(analysis.Deaths, found?.Analysis?.Deaths);
        Assert.Equal(analysis.Damage, found?.Analysis?.Damage);
        Assert.Equal(analysis.Purchases, found?.Analysis?.Purchases);
        Assert.Equal(analysis.Objectives, found?.Analysis?.Objectives);
        Assert.Equal(analysis.Metrics, found?.Analysis?.Metrics);
        var heatmap = Assert.Single(found!.Analysis!.Heatmaps!);
        Assert.Equal(31, heatmap.HeroId);
        Assert.Equal(analysis.Heatmaps![0].Phases.EarlyGame, heatmap.Phases.EarlyGame);
        Assert.Equal(analysis.Heatmaps[0].Phases.MidGame, heatmap.Phases.MidGame);
        Assert.Empty(heatmap.Phases.LateGame);
        Assert.Equal(analysis.Heatmaps[0].Proximity, heatmap.Proximity);
        Assert.Equal(analysis.AbilityUpgrades, found.Analysis.AbilityUpgrades);
    }

    [Fact]
    public async Task SaveThenFind_FailedAndExpired_HaveNoAnalysis()
    {
        var repository = CreateRepository();
        var otherMatch = ReplayBuilder.MatchIdOf(42);

        await repository.SaveAsync(SavedReplayAnalysis.Failed(Match, SavedAt), CancellationToken.None);
        await repository.SaveAsync(SavedReplayAnalysis.Expired(otherMatch, SavedAt), CancellationToken.None);

        var failed = await repository.FindAsync(Match, CancellationToken.None);
        var expired = await repository.FindAsync(otherMatch, CancellationToken.None);
        Assert.Equal(SavedReplayAnalysis.Failed(Match, SavedAt), failed);
        Assert.Equal(SavedReplayAnalysis.Expired(otherMatch, SavedAt), expired);

        // Only a ready result has data.
        await using var db = await postgres.DbContextFactory.CreateDbContextAsync();
        Assert.All(await db.ReplayAnalyses.ToListAsync(), row => Assert.Null(row.Data));
    }

    [Fact]
    public async Task Find_NeverParsed_ReturnsNull()
    {
        Assert.Null(await CreateRepository().FindAsync(Match, CancellationToken.None));
    }

    [Fact]
    public async Task Save_ReplacesTheEarlierResult()
    {
        // A failure tried again later may become ready.
        var repository = CreateRepository();
        var analysis = ReplayBuilder.Analysis(MatchIdValue, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom));

        await repository.SaveAsync(SavedReplayAnalysis.Failed(Match, SavedAt), CancellationToken.None);
        await repository.SaveAsync(SavedReplayAnalysis.Ready(analysis, SavedAt.AddHours(2)), CancellationToken.None);

        var found = await repository.FindAsync(Match, CancellationToken.None);
        Assert.Equal(SavedReplayState.Ready, found!.State);
        Assert.Equal(SavedAt.AddHours(2), found.SavedAt);
        await using var db = await postgres.DbContextFactory.CreateDbContextAsync();
        Assert.Single(await db.ReplayAnalyses.ToListAsync());
    }

    [Fact]
    public async Task Save_SameMatchManyTimesAtOnce_DoesNotFail()
    {
        var repository = CreateRepository();

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            repository.SaveAsync(SavedReplayAnalysis.Failed(Match, SavedAt), CancellationToken.None)));

        Assert.NotNull(await repository.FindAsync(Match, CancellationToken.None));
    }

    [Theory]
    [InlineData("ready", """{"version": 99}""")] // a format this version doesn't know
    [InlineData("ready", null)] // ready without data
    [InlineData("ready", """{"version": 1, "matchId": 9034320024, "endsAtSeconds": 9999, "players": []}""")] // breaks a rule
    [InlineData("ready", """{"version": 1, "matchId": 9034320024, "endsAtSeconds": 600, "players": [{"heroId": 1, "team": "radiant", "lane": "sideways"}]}""")]
    [InlineData("parsing", null)] // unknown status
    public async Task Find_UnreadableRow_CountsAsNeverParsed(string status, string? data)
    {
        // Not a database failure: the replay is simply parsed again (and the new result replaces the row).
        await using (var db = await postgres.DbContextFactory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO replay_analyses (match_id, status, data, saved_at) VALUES ({MatchIdValue}, {status}, CAST({data} AS jsonb), {SavedAt})");
        }

        var repository = CreateRepository();
        Assert.Null(await repository.FindAsync(Match, CancellationToken.None));

        await repository.SaveAsync(SavedReplayAnalysis.Expired(Match, SavedAt), CancellationToken.None);
        Assert.Equal(SavedReplayState.Expired, (await repository.FindAsync(Match, CancellationToken.None))!.State);
    }

    [Fact]
    public async Task Find_OldMetricJsonWithoutPhaseSlices_RemainsReadableAndOutdated()
    {
        const string oldJson = """
            {
              "version": 1,
              "matchId": 9034320024,
              "endsAtSeconds": 600,
              "players": [],
              "metrics": [{
                "heroId": 31,
                "team": "radiant",
                "role": 5,
                "deaths": 0,
                "isolatedDeaths": 0,
                "deathsOnEnemySide": 0,
                "enemySideShare": 0,
                "aloneOnEnemySideShare": 0,
                "netWorthAt10": 0,
                "netWorthAt20": null,
                "lastHitsAt10": 0,
                "netWorthShare": 0,
                "observersPlaced": 0,
                "sentriesPlaced": 0,
                "observerAverageLifetimeSeconds": null,
                "wardsLostToEnemy": 0,
                "enemyWardsDestroyed": 0,
                "timeByRegion": { "ownBase": 1, "enemyBase": 0, "topLane": 0, "middleLane": 0, "bottomLane": 0, "river": 0, "ownJungle": 0, "enemyJungle": 0 },
                "deathsByRegion": { "ownBase": 0, "enemyBase": 0, "topLane": 0, "middleLane": 0, "bottomLane": 0, "river": 0, "ownJungle": 0, "enemyJungle": 0 },
                "roleInferred": false,
                "estimatedMovementDistance": 0,
                "estimatedMovementByRegion": { "ownBase": 0, "enemyBase": 0, "topLane": 0, "middleLane": 0, "bottomLane": 0, "river": 0, "ownJungle": 0, "enemyJungle": 0 },
                "movementSampleSegments": 0
              }]
            }
            """;
        await using (var db = await postgres.DbContextFactory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO replay_analyses (match_id, status, data, saved_at) VALUES ({MatchIdValue}, 'ready', CAST({oldJson} AS jsonb), {SavedAt})");
        }

        var found = await CreateRepository().FindAsync(Match, CancellationToken.None);

        Assert.NotNull(found?.Analysis);
        var metrics = Assert.Single(found.Analysis.Metrics!);
        Assert.Null(metrics.FirstBigItemSeconds);
        Assert.Null(metrics.TimeByRegionByPhase);
        Assert.Null(metrics.DeathsByRegionByPhase);
        Assert.True(found.Analysis.IsOutdated);
    }

    private ReplayAnalysisRepository CreateRepository() =>
        new(postgres.DbContextFactory, new StorageGuard(TimeProvider.System), NullLogger<ReplayAnalysisRepository>.Instance);
}
