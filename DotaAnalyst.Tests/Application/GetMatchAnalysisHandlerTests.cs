using DotaAnalyst.Application.Baseline;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Domain.Replays;
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

    // ----- Laning phase from our own replay parsing -----

    private static readonly Uri ReplayUrl = new("http://replay202.valve.net/570/10_96247495.dem.bz2");

    [Fact]
    public async Task Laning_ParsedByOpenDota_IsQueuedToo()
    {
        // OpenDota's parse has neither the laning phase nor the timeline in the form we use.
        var queue = new FakeReplayParseQueue();
        var handler = CreateHandler(UnparsedMatchWithMe() with { IsParsed = true }, new FakeReplayAnalysisRepository(), queue);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Processing, analysis!.ReplayStatus);
        Assert.Equal([10], queue.Enqueued.Select(entry => entry.MatchId));
    }

    [Fact]
    public async Task Timeline_Ready_LeadsForThePlayersTeamAndTheirLane()
    {
        var replay = ReplayBuilder.Analysis(
            10,
            ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom, netWorth: 2047),
            ReplayBuilder.Hero(17, Team.Radiant, Lane.Bottom, netWorth: 3000),
            ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom, netWorth: 3797),
            ReplayBuilder.Hero(5, Team.Dire, Lane.Middle, netWorth: 5000));
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var timeline = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.Timeline;

        Assert.NotNull(timeline);
        Assert.Equal([0, 600, 1200], timeline.Seconds);
        Assert.Equal(Team.Radiant, timeline.Perspective);
        var lead = 2047 + 3000 - 3797 - 5000;
        Assert.Equal([0, lead, 2 * lead], timeline.NetWorthLead);
        Assert.Equal([0, 0, 0], timeline.ExperienceLead); // 2 heroes per team, same experience
        Assert.Equal(
            [(31, LaneSide.Player), (17, LaneSide.Ally), (28, LaneSide.Opponent)],
            timeline.Lane.Select(hero => (hero.HeroId, hero.Side)));
        Assert.Equal([0, 2047, 4094], timeline.Lane[0].NetWorth);
        Assert.Equal("Lich", timeline.Lane[0].HeroName);
    }

    [Fact]
    public async Task Timeline_PlayerNotInTheMatch_RadiantPerspectiveAndNoLane()
    {
        var replay = ReplayBuilder.Analysis(
            10, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom, netWorth: 3000),
            ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom, netWorth: 1000));
        var someoneElse = MatchPlayerBuilder.Create(accountId: MatchPlayerBuilder.Account(1), heroId: 31);
        var handler = CreateHandler(
            CreateMatch(Team.Radiant, someoneElse) with { ReplayUrl = ReplayUrl },
            new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var timeline = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.Timeline;

        Assert.NotNull(timeline);
        Assert.Equal(Team.Radiant, timeline.Perspective);
        Assert.Equal([0, 2000, 4000], timeline.NetWorthLead);
        Assert.Empty(timeline.Lane);
    }

    [Fact]
    public async Task Deaths_Ready_ThePlayersOnlyInTimeOrderWithKillers()
    {
        var players = new[] { ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom) };
        var replay = ReplayBuilder.AnalysisWithDeaths(
            10, players,
            new HeroDeath(31, 900, 100, -200, KillerKind.Tower, null, reincarnated: false),
            new HeroDeath(28, 300, 5000, -6000, KillerKind.Hero, 31, reincarnated: false),
            new HeroDeath(31, 240, 4800, -6100, KillerKind.Hero, 28, reincarnated: true));
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var deaths = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.Deaths;

        Assert.NotNull(deaths);
        Assert.Equal(
            [
                new PlayerDeath(240, 4800, -6100, KillerKind.Hero, 28, GetMatchAnalysisHandler.UnknownHeroName, Reincarnated: true, InLaningPhase: true),
                new PlayerDeath(900, 100, -200, KillerKind.Tower, null, null, Reincarnated: false, InLaningPhase: false),
            ],
            deaths);
    }

    [Fact]
    public async Task Wards_Ready_BothTeamsWithTheirSideAndHeroNames()
    {
        var players = new[] { ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom) };
        var replay = new ReplayAnalysis(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds, players, ReplayBuilder.Timeline(players), deaths: [],
            wards:
            [
                new WardPlacement(WardKind.Sentry, Team.Dire, 28, 100, 200, 700, 760, destroyed: true, 31),
                new WardPlacement(WardKind.Observer, Team.Radiant, 31, -3000, 1500, -60, null, destroyed: false, null),
            ],
            damage: [], purchases: [], objectives: [], metrics: [], heatmaps: []);
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var wards = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.Wards;

        Assert.Equal(
            [
                new MapWard(WardKind.Observer, Team.Radiant, IsPlayersTeam: true, "Lich", -3000, 1500, -60, null,
                    Destroyed: false, DestroyedByHeroName: null, InLaningPhase: true),
                new MapWard(WardKind.Sentry, Team.Dire, IsPlayersTeam: false, GetMatchAnalysisHandler.UnknownHeroName,
                    100, 200, 700, 760, Destroyed: true, DestroyedByHeroName: "Lich", InLaningPhase: false),
            ],
            wards);
    }

    // Me (Lich, 31) and an ally (5) on Radiant; two enemies (28, 2) on Dire.
    private static MatchDetails TeamfightMatch() =>
        CreateMatch(
            Team.Radiant,
            MatchPlayerBuilder.Create(accountId: Me, heroId: 31, team: Team.Radiant),
            MatchPlayerBuilder.Create(accountId: null, heroId: 5, team: Team.Radiant),
            MatchPlayerBuilder.Create(accountId: null, heroId: 28, team: Team.Dire),
            MatchPlayerBuilder.Create(accountId: null, heroId: 2, team: Team.Dire))
        with { ReplayUrl = ReplayUrl };

    private static ReplayAnalysis TeamfightReplay(IReadOnlyList<HeroDeath> deaths, IReadOnlyList<DamageRow> damage)
    {
        var players = new[]
        {
            ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(5, Team.Radiant, Lane.Bottom),
            ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom), ReplayBuilder.Hero(2, Team.Dire, Lane.Top),
        };
        return new ReplayAnalysis(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds, players, ReplayBuilder.Timeline(players), deaths,
            wards: [], damage, purchases: [], objectives: [], metrics: [], heatmaps: []);
    }

    [Fact]
    public async Task Heatmaps_Ready_EveryHeroRadiantFirst_AsFlatTriples()
    {
        static HeroHeatmap Heatmap(int heroId, params HeatCell[] early) =>
            new(heroId, new PerGamePhase<IReadOnlyList<HeatCell>>(early, [], [new HeatCell(16, 16, 3)]),
                new PerGamePhase<TeamProximity>(new TeamProximity(early.Sum(cell => cell.Samples), 0), default, default));
        var players = new[]
        {
            ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(5, Team.Radiant, Lane.Bottom),
            ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom), ReplayBuilder.Hero(2, Team.Dire, Lane.Top),
        };
        var replay = new ReplayAnalysis(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds, players, ReplayBuilder.Timeline(players), deaths: [],
            wards: [], damage: [], purchases: [], objectives: [], metrics: [],
            heatmaps: [Heatmap(28, new HeatCell(20, 5, 9)), Heatmap(31, new HeatCell(5, 25, 4), new HeatCell(6, 25, 1)), Heatmap(77)]);
        var handler = CreateHandler(
            TeamfightMatch(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var heatmaps = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.Heatmaps;

        Assert.NotNull(heatmaps);
        Assert.Equal((32, -9472, 9472, 2), (heatmaps.GridSize, heatmaps.MapMin, heatmaps.MapMax, heatmaps.SecondsPerSample));
        // Hero 77 isn't in the match: left out.
        Assert.Equal([(31, Team.Radiant, true), (28, Team.Dire, false)],
            heatmaps.Heroes.Select(hero => (hero.HeroId, hero.Team, hero.IsCurrentPlayer)));
        Assert.Equal([5, 25, 4, 6, 25, 1], heatmaps.Heroes[0].EarlyGame);
        Assert.Equal([16, 16, 3], heatmaps.Heroes[0].LateGame);
        Assert.Empty(heatmaps.Heroes[1].MidGame);

        // Indicators per phase: 5 early samples (10 s), all with an ally close by; no mid game.
        var mine = heatmaps.Heroes[0].Indicators!;
        Assert.Equal((10, 1.0), (mine.EarlyGame!.AliveSeconds, mine.EarlyGame.NearAlly!.Value));
        Assert.Null(mine.MidGame);
        Assert.Equal(16, mine.All!.AliveSeconds);
        // The match average of the two heroes in the match.
        Assert.Equal((10 + 18) / 2, heatmaps.MatchAverage!.EarlyGame!.AliveSeconds);
    }

    [Fact]
    public async Task Damage_Ready_ThePlayersToAndFromEnemyHeroes_ByHeroAndByInflictor()
    {
        const string Hero = DamageRow.HeroKind;
        var replay = TeamfightReplay(
            deaths: [],
            damage:
            [
                new DamageRow(31, Hero, 28, Hero, "lich_frost_nova", 1000),
                new DamageRow(31, "summons", 28, Hero, null, 200),
                new DamageRow(31, Hero, 2, Hero, null, 1500),
                new DamageRow(31, Hero, 2, "illusion", "lich_frost_nova", 999), // an illusion isn't the hero
                new DamageRow(31, Hero, 5, Hero, null, 50), // the ally (deny)
                new DamageRow(31, Hero, null, "laneCreeps", null, 5000),
                new DamageRow(28, "illusion", 31, Hero, null, 700),
                new DamageRow(2, Hero, 31, "summons", null, 888), // hit my summon, not me
                new DamageRow(null, "buildings", 31, Hero, null, 400),
                new DamageRow(28, Hero, 5, Hero, null, 600), // the ally took it
            ]);
        var handler = CreateHandler(
            TeamfightMatch(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var damage = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.Damage;

        Assert.NotNull(damage);
        var unknown = GetMatchAnalysisHandler.UnknownHeroName;
        Assert.Equal([new HeroAmount(2, unknown, 1500), new HeroAmount(28, unknown, 1200)], damage.ToHeroes);
        Assert.Equal([new HeroAmount(28, unknown, 700)], damage.FromHeroes);
        Assert.Equal([new InflictorAmount(null, 1700), new InflictorAmount("lich_frost_nova", 1000)], damage.ByInflictor);
    }

    [Fact]
    public async Task Damage_ItemInflictors_ComeWithTheItemsIdAndName()
    {
        var replay = TeamfightReplay(
            deaths: [],
            damage:
            [
                new DamageRow(31, DamageRow.HeroKind, 28, DamageRow.HeroKind, "item_dagon_5", 800),
                new DamageRow(31, DamageRow.HeroKind, 28, DamageRow.HeroKind, "lich_frost_nova", 500),
            ]);
        var items = new FakeItemCatalog(new Item(104, "Dagon 5") { Details = new ItemDetails("dagon_5", 7050, [], isConsumable: false) });
        var handler = CreateHandler(
            new FakeReplayAnalyzer(TeamfightMatch()),
            replays: new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)),
            items: items);

        var damage = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.Damage!;

        Assert.Equal(
            [new InflictorAmount("item_dagon_5", 800, 104, "Dagon 5"), new InflictorAmount("lich_frost_nova", 500)],
            damage.ByInflictor);
    }

    [Fact]
    public async Task Fights_Ready_WithThePlayersPart()
    {
        var replay = TeamfightReplay(
            deaths:
            [
                // A fight at 10:00 won by Radiant: I killed one and assisted on the other, then died.
                new HeroDeath(28, 600, 100, 100, KillerKind.Hero, 31, reincarnated: false, assistHeroIds: [5]),
                new HeroDeath(2, 605, 300, 100, KillerKind.Hero, 5, reincarnated: false, assistHeroIds: [31]),
                new HeroDeath(31, 612, 200, 400, KillerKind.Hero, 28, reincarnated: false),
                // A pick-off, not a fight.
                new HeroDeath(5, 1200, 0, 0, KillerKind.Hero, 2, reincarnated: false),
            ],
            damage: []);
        var handler = CreateHandler(
            TeamfightMatch(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var fights = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.Fights;

        Assert.Equal(
            [
                new FightSummary(600, 612, RadiantDeaths: 1, DireDeaths: 2, Team.Radiant, PlayersTeamWon: true,
                    Died: true, Kills: 1, Assists: 1, X: 200, Y: 200),
            ],
            fights);
    }

    [Fact]
    public async Task ItemBuild_Ready_ThePlayersStartingItemsAndBuild_WithNames()
    {
        var players = new[] { ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom) };
        var replay = new ReplayAnalysis(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds, players, ReplayBuilder.Timeline(players), deaths: [],
            wards: [], damage: [],
            purchases:
            [
                new ItemPurchase(31, -90, "tango"),
                new ItemPurchase(31, 600, "ogre_axe"),
                new ItemPurchase(28, 700, "blink"), // the enemy's
                new ItemPurchase(31, 900, "mithril_hammer"),
                new ItemPurchase(31, 1000, "recipe_black_king_bar"),
                new ItemPurchase(31, 1003, "black_king_bar"), // logged when the parts came together
            ],
            objectives: [], metrics: [], heatmaps: []);
        var items = new[]
        {
            new Item(44, "Tango") { Details = new ItemDetails("tango", 90, [], isConsumable: true) },
            new Item(1, "Blink Dagger") { Details = new ItemDetails("blink", 2250, [], isConsumable: false) },
            new Item(2, "Ogre Axe") { Details = new ItemDetails("ogre_axe", 1000, [], isConsumable: false) },
            new Item(3, "Mithril Hammer") { Details = new ItemDetails("mithril_hammer", 1600, [], isConsumable: false) },
            new Item(4, "Black King Bar Recipe") { Details = new ItemDetails("recipe_black_king_bar", 1450, [], isConsumable: false) },
            new Item(116, "Black King Bar") { Details = new ItemDetails("black_king_bar", 4050, ["mithril_hammer", "ogre_axe"], isConsumable: false) },
        };
        var handler = CreateHandler(
            new FakeReplayAnalyzer(UnparsedMatchWithMe()),
            items: new FakeItemCatalog(items),
            replays: new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var build = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.ItemBuild;

        Assert.NotNull(build);
        Assert.Equal([new StartingItemCount(44, "Tango", 1)], build.StartingItems);
        Assert.Equal([new TimedItem(116, "Black King Bar", 1003, Assembled: true)], build.Items);
    }

    [Fact]
    public async Task Objectives_Ready_BothTeamsWithTheirSideAndHeroNames()
    {
        var players = new[] { ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom) };
        var replay = new ReplayAnalysis(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds, players, ReplayBuilder.Timeline(players), deaths: [],
            wards: [], damage: [], purchases: [],
            objectives:
            [
                new MapObjective(ObjectiveKind.Roshan, 1300, Team.Radiant, 31, aegisHeroId: 31),
                new MapObjective(ObjectiveKind.Tower, 680, Team.Dire, 28, Team.Radiant, towerTier: 1, lane: Lane.Bottom),
            ],
            metrics: [], heatmaps: []);
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var objectives = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.Objectives;

        Assert.Equal(
            [
                new MapObjectiveSummary(ObjectiveKind.Tower, 680, Team.Dire, IsPlayersTeam: false, Team.Radiant, 1, Lane.Bottom,
                    MeleeBarracks: null, Denied: false, GetMatchAnalysisHandler.UnknownHeroName, AegisHeroName: null),
                new MapObjectiveSummary(ObjectiveKind.Roshan, 1300, Team.Radiant, IsPlayersTeam: true, Owner: null, TowerTier: null,
                    Lane: null, MeleeBarracks: null, Denied: false, "Lich", "Lich"),
            ],
            objectives);
    }

    private static HeroMetrics HeroMetricsOf(int heroId, Team team, int role, int isolatedDeaths = 0, int deaths = 2) =>
        new(heroId, team, role, deaths, isolatedDeaths, 0, 0.2, 0.02, 2000, null, 10, 0.2, 0, 0, null, 0, 0,
            PerRegion<double>.Of(_ => 0.125), PerRegion<int>.Of(_ => 0), EstimatedMovementDistance: 1000,
            EstimatedMovementByRegion: PerRegion<double>.Of(_ => 125), MovementSampleSegments: 100,
            TimeByRegionByPhase: new PerGamePhase<PerRegion<double>>(
                PerRegion<double>.Of(_ => 0.25), PerRegion<double>.Of(_ => 0.5), PerRegion<double>.Of(_ => 0.75)),
            DeathsByRegionByPhase: new PerGamePhase<PerRegion<int>>(
                PerRegion<int>.Of(_ => 1), PerRegion<int>.Of(_ => 2), PerRegion<int>.Of(_ => 3)),
            TeamFights: 4, FightsNearby: 2, KillParticipation: 0.5, QuickRedeaths: 0,
            LaneDamageDealt: 2000, LaneDamageTaken: 2000, TeamObserverUptime: 0.6,
            NetWorthGainByRegion: PerRegion<double>.Of(_ => 1000), MinutesByRegion: PerRegion<int>.Of(_ => 3));

    [Fact]
    public async Task SmartStats_ComparedWithTheEnemyOfTheSameRole()
    {
        var players = new[] { ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom) };
        var replay = new ReplayAnalysis(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds, players, ReplayBuilder.Timeline(players), deaths: [],
            wards: [], damage: [], purchases: [], objectives: [],
            metrics:
            [
                HeroMetricsOf(31, Team.Radiant, role: 5, isolatedDeaths: 3, deaths: 5),
                HeroMetricsOf(2, Team.Dire, role: 1),
                HeroMetricsOf(28, Team.Dire, role: 5, isolatedDeaths: 1, deaths: 3),
            ],
            heatmaps: []);
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var stats = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.SmartStats;

        Assert.NotNull(stats);
        Assert.Equal((5, 28, GetMatchAnalysisHandler.UnknownHeroName), (stats.Role, stats.CounterpartHeroId, stats.CounterpartHeroName));
        Assert.Equal((5, 3), (stats.Player.Deaths, stats.Counterpart.Deaths));
        Assert.Equal(10.0 / 3, stats.MatchAverage.Deaths, precision: 6);
        Assert.Null(stats.MatchAverage.NetWorthAt20);
        Assert.Equal(0.125, stats.Player.TimeByRegion!.EnemyJungle);
        Assert.Equal(0.125, stats.MatchAverage.TimeByRegion!.River);
        Assert.Equal(0, stats.Counterpart.DeathsByRegion!.EnemyJungle);
        Assert.Equal(0.25, stats.Player.TimeByRegionByPhase!.EarlyGame.OwnBase);
        Assert.Equal(0.5, stats.Player.TimeByRegionByPhase.MidGame.OwnBase);
        Assert.Equal(0.75, stats.MatchAverage.TimeByRegionByPhase!.LateGame.OwnBase);
        Assert.Equal(2, stats.Player.DeathsByRegionByPhase!.MidGame.OwnBase);
        Assert.Equal(1, stats.MatchAverage.DeathsByRegionByPhase!.EarlyGame.OwnBase);
        Assert.Equal([new InsightSummary(InsightKind.IsolatedDeaths, 3, 1)], stats.Insights);
    }

    private static ReplayAnalysis SupportDuel() =>
        new(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds,
            [ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom)],
            ReplayBuilder.Timeline([ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom)]),
            deaths: [], wards: [], damage: [], purchases: [], objectives: [],
            metrics: [HeroMetricsOf(31, Team.Radiant, role: 5, deaths: 2), HeroMetricsOf(28, Team.Dire, role: 5)], heatmaps: []);

    [Fact]
    public async Task SmartStats_ComparedWithTheBase_OfTheSameHeroAndRole()
    {
        // Ten pro Lich supports with 1..10 deaths in 30-minute matches; the player's match lasts 1725 s.
        var pro = Enumerable.Range(1, 10)
            .Select(deaths => new BaselineSample(HeroMetricsOf(31, Team.Dire, role: 5, deaths: deaths), 1800))
            .ToList();
        var handler = CreateHandler(
            new FakeReplayAnalyzer(UnparsedMatchWithMe()),
            replays: new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(SupportDuel(), Now)),
            baseline: new FakeDistributions(BaselineSource.Pro, pro.Count, BaselineComparer.Distribute(pro)));

        var baselines = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.SmartStats!.Baselines;

        Assert.NotNull(baselines);
        Assert.Equal([BaselineSource.Pro, BaselineSource.HighRank], baselines.Select(baseline => baseline.Source));
        var deaths = Assert.Single(baselines[0].Metrics, metric => metric.Metric == ComparedMetric.DeathsPer30);
        Assert.Equal(5.5, deaths.Median);
        Assert.True(deaths.BetterThan > 0.7); // ~2.1 deaths per 30 min against 1..10
        Assert.Equal(0, baselines[1].Matches);
        Assert.Empty(baselines[1].Metrics);
    }

    [Fact]
    public async Task SmartStats_BaseUnreadable_ComesWithoutIt()
    {
        var handler = CreateHandler(
            new FakeReplayAnalyzer(UnparsedMatchWithMe()),
            replays: new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(SupportDuel(), Now)),
            baseline: new FakeDistributions { Fail = true });

        var stats = (await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.SmartStats;

        Assert.NotNull(stats);
        Assert.Null(stats.Baselines);
    }

    [Fact]
    public async Task SmartStats_WithInferredRoles_IsNull()
    {
        var players = new[] { ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom), ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom) };
        var replay = new ReplayAnalysis(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds, players, ReplayBuilder.Timeline(players), deaths: [],
            wards: [], damage: [], purchases: [], objectives: [],
            metrics:
            [
                HeroMetricsOf(31, Team.Radiant, role: 5) with { RoleInferred = true },
                HeroMetricsOf(28, Team.Dire, role: 5) with { RoleInferred = true },
            ],
            heatmaps: []);
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        Assert.Null((await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None))!.SmartStats);
    }

    [Fact]
    public async Task SmartStats_WithoutRoleQueue_IsNull()
    {
        var replay = ReplayBuilder.Analysis(10, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom)); // metrics: []
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Ready, analysis!.ReplayStatus);
        Assert.Null(analysis.SmartStats);
    }

    [Fact]
    public async Task ItemBuild_CatalogWithoutDetails_IsNull()
    {
        var replay = ReplayBuilder.Analysis(10, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom));
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)));

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Ready, analysis!.ReplayStatus);
        Assert.Null(analysis.ItemBuild);
    }

    [Fact]
    public async Task Damage_SavedWithoutIt_IsParsedAgain()
    {
        var players = new[] { ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom) };
        var withoutDamage = new ReplayAnalysis(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds, players, ReplayBuilder.Timeline(players), deaths: [],
            wards: []);
        var queue = new FakeReplayParseQueue();
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(withoutDamage, Now)), queue);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Processing, analysis!.ReplayStatus);
        Assert.Equal([10], queue.Enqueued.Select(entry => entry.MatchId));
        Assert.Null(analysis.Fights);
    }

    [Fact]
    public async Task Deaths_SavedWithoutThem_IsParsedAgain()
    {
        var players = new[] { ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom) };
        var withoutDeaths = new ReplayAnalysis(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds, players, ReplayBuilder.Timeline(players));
        var queue = new FakeReplayParseQueue();
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(withoutDeaths, Now)), queue);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Processing, analysis!.ReplayStatus);
        Assert.Equal([10], queue.Enqueued.Select(entry => entry.MatchId));
    }

    [Fact]
    public async Task Timeline_SavedWithoutIt_IsParsedAgain()
    {
        var replay = ReplayBuilder.AnalysisWithoutTimeline(10, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom));
        var queue = new FakeReplayParseQueue();
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)), queue);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Processing, analysis!.ReplayStatus);
        Assert.Equal([10], queue.Enqueued.Select(entry => entry.MatchId));
    }

    [Fact]
    public async Task Timeline_SavedWithoutItAndCannotQueue_KeepsTheLaningPhase()
    {
        var replay = ReplayBuilder.AnalysisWithoutTimeline(10, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom));
        var queue = new FakeReplayParseQueue { Result = ReplayEnqueueResult.Unavailable };
        var handler = CreateHandler(
            UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now)), queue);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Ready, analysis!.ReplayStatus);
        Assert.NotNull(analysis.Laning);
        Assert.Null(analysis.Timeline);
    }

    [Fact]
    public async Task Laning_AlreadyQueued_IsProcessingWithoutAskingTheDatabase()
    {
        var replays = new FakeReplayAnalysisRepository { Fail = true };
        var queue = new FakeReplayParseQueue { Queued = { 10 } };
        var handler = CreateHandler(UnparsedMatchWithMe(), replays, queue);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Processing, analysis!.ReplayStatus);
        Assert.Equal(0, replays.FindCalls);
    }

    [Fact]
    public async Task Laning_Ready_FillsTheLaningPhaseOfTheCurrentPlayer()
    {
        var replay = ReplayBuilder.Analysis(
            10,
            ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom, netWorth: 2047),
            ReplayBuilder.Hero(17, Team.Radiant, Lane.Bottom, netWorth: 3000),
            ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom, netWorth: 3797),
            ReplayBuilder.Hero(5, Team.Dire, Lane.Middle, netWorth: 5000));
        var replays = new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now.AddDays(-3)));
        var queue = new FakeReplayParseQueue();
        var handler = CreateHandler(UnparsedMatchWithMe(), replays, queue);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Ready, analysis!.ReplayStatus);
        var laning = analysis.Laning;
        Assert.NotNull(laning);
        Assert.Equal(600, laning.EndsAtSeconds);
        Assert.Equal(Lane.Bottom, laning.Lane);
        Assert.Equal(LaneOutcome.Won, laning.Outcome);
        Assert.Equal(2047 + 3000 - 3797, laning.NetWorthDifference);
        Assert.Equal(new LaningPlayer(31, "Lich", 20, 3, 2047, 2500, 1), laning.Player);
        Assert.Equal([17], laning.Allies.Select(ally => ally.HeroId));
        Assert.Equal(GetMatchAnalysisHandler.UnknownHeroName, laning.Allies[0].HeroName);
        Assert.Equal([28], laning.Opponents.Select(opponent => opponent.HeroId));
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task Laning_ReadyButPlayerNotInTheMatch_HasNoLaning()
    {
        var replay = ReplayBuilder.Analysis(10, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom));
        var replays = new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now));
        var someoneElse = MatchPlayerBuilder.Create(accountId: MatchPlayerBuilder.Account(1), heroId: 31);
        var handler = CreateHandler(CreateMatch(Team.Radiant, someoneElse) with { ReplayUrl = ReplayUrl }, replays);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Ready, analysis!.ReplayStatus);
        Assert.Null(analysis.Laning);
    }

    [Fact]
    public async Task Laning_ReadyButHeroNotInTheReplay_HasNoLaning()
    {
        var replay = ReplayBuilder.Analysis(10, ReplayBuilder.Hero(99, Team.Radiant, Lane.Bottom));
        var replays = new FakeReplayAnalysisRepository(SavedReplayAnalysis.Ready(replay, Now));
        var handler = CreateHandler(UnparsedMatchWithMe(), replays);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Ready, analysis!.ReplayStatus);
        Assert.Null(analysis.Laning);
    }

    [Fact]
    public async Task Laning_Expired_IsNeverQueuedAgain()
    {
        var replays = new FakeReplayAnalysisRepository(SavedReplayAnalysis.Expired(MatchIdOf(10), Now.AddDays(-30)));
        var queue = new FakeReplayParseQueue();
        var handler = CreateHandler(UnparsedMatchWithMe(), replays, queue);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Expired, analysis!.ReplayStatus);
        Assert.Null(analysis.Laning);
        Assert.Empty(queue.Enqueued);
    }

    [Theory]
    [InlineData(23 * 60 + 59, 59, ReplayStatus.Failed, false)]     // missing too soon after the match: a failure
    [InlineData(23 * 60 + 59, 60, ReplayStatus.Processing, true)]  // ... queued again an hour later
    [InlineData(24 * 60, 59, ReplayStatus.Expired, false)]         // missing a day after the match: expired for good
    [InlineData(24 * 60, 60 * 24 * 30, ReplayStatus.Expired, false)]
    public async Task Laning_ExpiredRightAfterTheMatch_IsTreatedAsFailed(
        int savedMinutesAfterTheMatch, int savedMinutesAgo, ReplayStatus expected, bool queuedAgain)
    {
        // Valve uploads the replay some time after the match: right after it, "not found" may mean "not yet".
        var match = UnparsedMatchWithMe();
        var savedAt = match.EndedAt.AddMinutes(savedMinutesAfterTheMatch);
        var replays = new FakeReplayAnalysisRepository(SavedReplayAnalysis.Expired(MatchIdOf(10), savedAt));
        var queue = new FakeReplayParseQueue();
        var handler = CreateHandler(match, replays, queue, new FixedTimeProvider(savedAt.AddMinutes(savedMinutesAgo)));

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(TimeSpan.FromDays(1), ReplayAvailability.UploadWindow);
        Assert.Equal(expected, analysis!.ReplayStatus);
        Assert.Equal(queuedAgain, queue.Enqueued.Count == 1);
    }

    [Theory]
    [InlineData(59, ReplayStatus.Failed, false)]     // failed recently: shown as failed
    [InlineData(60, ReplayStatus.Processing, true)]  // an hour later: queued again
    public async Task Laning_Failed_IsQueuedAgainAfterAnHour(int failedMinutesAgo, ReplayStatus expected, bool queuedAgain)
    {
        var failedAt = Now.AddMinutes(-failedMinutesAgo);
        var replays = new FakeReplayAnalysisRepository(SavedReplayAnalysis.Failed(MatchIdOf(10), failedAt));
        var queue = new FakeReplayParseQueue();
        var handler = CreateHandler(UnparsedMatchWithMe(), replays, queue, new FixedTimeProvider(Now));

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(1), GetMatchAnalysisHandler.FailedReplayRetryInterval);
        Assert.Equal(expected, analysis!.ReplayStatus);
        Assert.Equal(queuedAgain, queue.Enqueued.Count == 1);
    }

    [Theory]
    [InlineData(ReplayEnqueueResult.Full)]
    [InlineData(ReplayEnqueueResult.Unavailable)]
    public async Task Laning_OldFailureThatCantBeQueuedNow_StaysFailed(ReplayEnqueueResult enqueueResult)
    {
        var replays = new FakeReplayAnalysisRepository(SavedReplayAnalysis.Failed(MatchIdOf(10), Now.AddDays(-1)));
        var handler = CreateHandler(UnparsedMatchWithMe(), replays, new FakeReplayParseQueue { Result = enqueueResult });

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Failed, analysis!.ReplayStatus);
    }

    [Theory]
    [InlineData(ReplayEnqueueResult.Enqueued, ReplayStatus.Processing)]
    [InlineData(ReplayEnqueueResult.AlreadyQueued, ReplayStatus.Processing)]
    [InlineData(ReplayEnqueueResult.Full, ReplayStatus.Unavailable)]
    [InlineData(ReplayEnqueueResult.Unavailable, ReplayStatus.Unavailable)]
    public async Task Laning_NeverParsed_IsQueued(ReplayEnqueueResult enqueueResult, ReplayStatus expected)
    {
        var queue = new FakeReplayParseQueue { Result = enqueueResult };
        var handler = CreateHandler(UnparsedMatchWithMe(), new FakeReplayAnalysisRepository(), queue);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(expected, analysis!.ReplayStatus);
        Assert.Null(analysis.Laning);
        Assert.Equal((10L, ReplayUrl), Assert.Single(queue.Enqueued));
    }

    [Fact]
    public async Task Laning_WithoutReplayUrl_IsUnavailable()
    {
        var queue = new FakeReplayParseQueue();
        var handler = CreateHandler(UnparsedMatchWithMe() with { ReplayUrl = null }, new FakeReplayAnalysisRepository(), queue);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.Equal(ReplayStatus.Unavailable, analysis!.ReplayStatus);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task Laning_DatabaseFails_IsUnavailableAndTheAnalysisStillAnswers()
    {
        var queue = new FakeReplayParseQueue();
        var handler = CreateHandler(UnparsedMatchWithMe(), new FakeReplayAnalysisRepository { Fail = true }, queue);

        var analysis = await handler.HandleAsync(MatchIdOf(10), Me, CancellationToken.None);

        Assert.NotNull(analysis);
        Assert.NotNull(analysis.Performance);
        Assert.Equal(ReplayStatus.Unavailable, analysis.ReplayStatus);
        // Nothing queued: the result could not be saved either.
        Assert.Empty(queue.Enqueued);
    }

    // An unparsed match with a replay address, where the current player plays Lich (hero 31) on Radiant.
    private static MatchDetails UnparsedMatchWithMe() =>
        CreateMatch(Team.Radiant, MatchPlayerBuilder.Create(accountId: Me, heroId: 31, team: Team.Radiant))
            with { ReplayUrl = ReplayUrl };

    private static GetMatchAnalysisHandler CreateHandler(
        MatchDetails match,
        FakeReplayAnalysisRepository replays,
        FakeReplayParseQueue? queue = null,
        TimeProvider? clock = null) =>
        CreateHandler(new FakeReplayAnalyzer(match), replays: replays, queue: queue, clock: clock);

    private static GetMatchAnalysisHandler CreateHandler(MatchDetails? match) =>
        CreateHandler(new FakeReplayAnalyzer(match));

    private static GetMatchAnalysisHandler CreateHandler(
        FakeReplayAnalyzer analyzer,
        FakeMatchRepository? repository = null,
        FakeReplayAnalysisRepository? replays = null,
        FakeReplayParseQueue? queue = null,
        TimeProvider? clock = null,
        FakeItemCatalog? items = null,
        FakeDistributions? baseline = null) =>
        new(
            analyzer,
            repository ?? new FakeMatchRepository(),
            replays ?? new FakeReplayAnalysisRepository(),
            queue ?? new FakeReplayParseQueue(),
            new FakeHeroCatalog(new Hero(31, "Lich")),
            // Names only by default (no details: no item build).
            items ?? new FakeItemCatalog(new Item(1, "Blink Dagger"), new Item(2, "Trusty Shovel")),
            new DistributionBaselineProvider(baseline ?? new FakeDistributions(), NullLogger<DistributionBaselineProvider>.Instance),
            clock ?? new FixedTimeProvider(Now),
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

    private class FakeReplayAnalysisRepository(SavedReplayAnalysis? saved = null) : IReplayAnalysisRepository
    {
        public bool Fail { get; init; }

        public int FindCalls { get; private set; }

        public Task<SavedReplayAnalysis?> FindAsync(MatchId matchId, CancellationToken cancellationToken)
        {
            FindCalls++;
            return Fail ? throw new StorageUnavailableException("The database is down.") : Task.FromResult(saved);
        }

        public Task SaveAsync(SavedReplayAnalysis result, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The analysis never saves replay results.");
    }

    private class FakeReplayParseQueue : IReplayParseQueue
    {
        public ReplayEnqueueResult Result { get; init; } = ReplayEnqueueResult.Enqueued;

        public HashSet<long> Queued { get; } = [];

        public List<(long MatchId, Uri ReplayUrl)> Enqueued { get; } = [];

        public ReplayEnqueueResult TryEnqueue(MatchId matchId, Uri replayUrl)
        {
            Enqueued.Add((matchId.Value, replayUrl));
            return Result;
        }

        public bool IsQueued(MatchId matchId) => Queued.Contains(matchId.Value);
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

    // The finished comparison base: these distributions for any hero/role of the given source.
    private class FakeDistributions(BaselineSource? source = null, int matches = 0, IReadOnlyList<MetricDistribution>? distributions = null)
        : IBaselineDistributionStore
    {
        public bool Fail { get; init; }

        public Task<BaselineDistributionSet?> FindAsync(int heroId, int role, BaselineSource requested, CancellationToken cancellationToken) =>
            Fail
                ? throw new StorageUnavailableException("The database is down.")
                : Task.FromResult(requested == source ? new BaselineDistributionSet(heroId, role, requested, matches, distributions ?? []) : null);

        public Task ReplaceAllAsync(IReadOnlyList<BaselineDistributionSet> sets, DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}