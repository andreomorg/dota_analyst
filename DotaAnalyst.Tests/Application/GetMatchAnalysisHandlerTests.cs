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
            ]);
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
        TimeProvider? clock = null) =>
        new(
            analyzer,
            repository ?? new FakeMatchRepository(),
            replays ?? new FakeReplayAnalysisRepository(),
            queue ?? new FakeReplayParseQueue(),
            new FakeHeroCatalog(new Hero(31, "Lich")),
            new FakeItemCatalog(new Item(1, "Blink Dagger"), new Item(2, "Trusty Shovel")),
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
}
