using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Tests.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Application;

public class CollectBaselineHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri ReplayUrl = new("http://replay.valve.net/570/1_2.dem.bz2");

    [Fact]
    public async Task ProMatchesFirst_SkippingKnownOnes_SavesTheTenHeroes()
    {
        var repository = new FakeBaseline { Known = { 1 } };
        var parser = new FakeParser();
        var handler = CreateHandler(repository, parser, pro: [1, 2], highRank: [3]);

        var result = await handler.CollectNextAsync(CancellationToken.None);

        Assert.Equal(CollectResult.Progressed, result);
        var saved = Assert.Single(repository.Saved);
        Assert.Equal((2L, BaselineSource.Pro, BaselineMatchStatus.Collected), (saved.MatchId.Value, saved.Source, saved.Status));
        Assert.Single(saved.Metrics);
        Assert.Equal([2L], parser.Parsed);
    }

    [Fact]
    public async Task WhenProMatchesAreAllKnown_HighRankComesNext()
    {
        var repository = new FakeBaseline { Known = { 1 } };
        var handler = CreateHandler(repository, new FakeParser(), pro: [1], highRank: [3]);

        await handler.CollectNextAsync(CancellationToken.None);

        Assert.Equal(BaselineSource.HighRank, Assert.Single(repository.Saved).Source);
    }

    [Fact]
    public async Task WithoutAReplay_IsSavedAsNoReplay_WithoutParsing()
    {
        var repository = new FakeBaseline();
        var parser = new FakeParser();
        var handler = CreateHandler(repository, parser, pro: [2], withReplay: false);

        await handler.CollectNextAsync(CancellationToken.None);

        Assert.Equal(BaselineMatchStatus.NoReplay, Assert.Single(repository.Saved).Status);
        Assert.Empty(parser.Parsed);
    }

    [Fact]
    public async Task ParserBusy_SavesNothing_SoTheMatchIsTriedAgain()
    {
        var repository = new FakeBaseline();
        var handler = CreateHandler(repository, new FakeParser { Throw = new ReplayParserBusyException("busy") }, pro: [2]);

        Assert.Equal(CollectResult.SourceUnavailable, await handler.CollectNextAsync(CancellationToken.None));
        Assert.Empty(repository.Saved);
    }

    [Fact]
    public async Task TheDailyLimit_StopsTheCollection()
    {
        var handler = CreateHandler(new FakeBaseline { CollectedToday = 300 }, new FakeParser(), pro: [2]);

        Assert.Equal(CollectResult.LimitReached, await handler.CollectNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task NothingNew_WhenEveryCandidateIsKnown()
    {
        var handler = CreateHandler(new FakeBaseline { Known = { 1, 3 } }, new FakeParser(), pro: [1], highRank: [3]);

        Assert.Equal(CollectResult.NothingNew, await handler.CollectNextAsync(CancellationToken.None));
    }

    private static CollectBaselineHandler CreateHandler(
        FakeBaseline repository, FakeParser parser, long[] pro, long[]? highRank = null, bool withReplay = true) =>
        new(
            new FakeSource(pro, highRank ?? []),
            repository,
            new FakeAnalyzer(withReplay ? ReplayUrl : null),
            parser,
            new FakeMatches(),
            new BaselineCollectorSettings(300, []),
            new FixedTimeProvider(Now),
            NullLogger<CollectBaselineHandler>.Instance);

    private sealed class FakeSource(long[] pro, long[] highRank) : IBaselineMatchSource
    {
        public Task<IReadOnlyList<BaselineCandidate>> GetCandidatesAsync(
            BaselineSource source, IReadOnlySet<int> focusHeroes, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BaselineCandidate>>(
                (source == BaselineSource.Pro ? pro : highRank)
                    .Select(id => new BaselineCandidate(ReplayBuilder.MatchIdOf(id), source))
                    .ToList());
    }

    private sealed class FakeBaseline : IBaselineRepository
    {
        public HashSet<long> Known { get; } = [];

        public int CollectedToday { get; init; }

        public List<BaselineMatch> Saved { get; } = [];

        public Task<IReadOnlySet<long>> FindKnownAsync(IReadOnlyCollection<MatchId> matchIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>>(Known);

        public Task SaveAsync(BaselineMatch match, CancellationToken cancellationToken)
        {
            Saved.Add(match);
            return Task.CompletedTask;
        }

        public Task<int> CountCollectedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult(CollectedToday);

        public Task<IReadOnlyList<(int HeroId, int Role, BaselineSource Source)>> GetGroupsAsync(
            DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(int, int, BaselineSource)>>([]);

        public Task<IReadOnlyList<BaselineSample>> GetSamplesAsync(
            int heroId, int role, BaselineSource source, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BaselineSample>>([]);
    }

    private sealed class FakeAnalyzer(Uri? replayUrl) : IReplayAnalyzer
    {
        public Task<MatchDetails?> GetMatchDetailsAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult<MatchDetails?>(new MatchDetails(
                matchId, TimeSpan.FromMinutes(35), Now.AddHours(-2), Team.Radiant, 30, 20, IsParsed: false, [])
            {
                ReplayUrl = replayUrl,
            });
    }

    private sealed class FakeParser : IReplayParser
    {
        public Exception? Throw { get; init; }

        public List<long> Parsed { get; } = [];

        public Task<ReplayAnalysis> ParseAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            Parsed.Add(matchId.Value);
            var player = ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom);
            return Task.FromResult(new ReplayAnalysis(
                matchId, ReplayAnalysis.LaningPhaseEndsAtSeconds, [player], ReplayBuilder.Timeline([player]), [], [], [], [], [],
                [new HeroMetrics(31, Team.Radiant, 5, 1, 0, 0, 0, 0, 2000, null, 10, 1, 0, 0, null, 0, 0, RoleInferred: true)]));
        }
    }

    private sealed class FakeMatches : IMatchProvider
    {
        public Task<IReadOnlyList<PlayerMatch>> GetRecentMatchesAsync(AccountId accountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlayerMatch>>([]);
    }
}
