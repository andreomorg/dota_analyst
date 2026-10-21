using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Tests.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Application;

public class RefreshReplayHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);
    private static readonly MatchId Match = ReplayBuilder.MatchIdOf(9043305627);
    private static readonly Uri SavedUrl = new("http://replay202.valve.net/570/9043305627_1.dem.bz2");
    private static readonly Uri OpenDotaUrl = new("http://replay191.valve.net/570/9043305627_2.dem.bz2");

    [Fact]
    public async Task Refreshed_SavesReady_UsingTheSavedMatchsUrl_AndTheFreshParse()
    {
        var replay = ReplayBuilder.Analysis(Match.Value, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom));
        var parser = new FakeParser(() => replay);
        var analyses = new FakeAnalyses();
        var openDota = new FakeOpenDota(OpenDotaUrl);

        var result = await Handler(parser, analyses, saved: SavedUrl, openDota).HandleAsync(Match, CancellationToken.None);

        Assert.Equal(RefreshReplayResult.Refreshed, result);
        // The fresh parse, never the plain one (which would reuse a kept document without the new sections).
        Assert.Equal([(Match, SavedUrl)], parser.FreshCalls);
        Assert.Equal(0, parser.PlainCalls);
        Assert.Equal(0, openDota.Calls);
        var saved = Assert.Single(analyses.Saved);
        Assert.Equal((SavedReplayState.Ready, Now), (saved.State, saved.SavedAt));
        Assert.Same(replay, saved.Analysis);
    }

    [Fact]
    public async Task WithoutASavedMatch_TheUrlComesFromOpenDota()
    {
        var parser = new FakeParser(() => ReplayBuilder.Analysis(Match.Value, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom)));

        var result = await Handler(parser, new FakeAnalyses(), saved: null, new FakeOpenDota(OpenDotaUrl))
            .HandleAsync(Match, CancellationToken.None);

        Assert.Equal(RefreshReplayResult.Refreshed, result);
        Assert.Equal([(Match, OpenDotaUrl)], parser.FreshCalls);
    }

    [Fact]
    public async Task NoUrlAnywhere_IsNoReplay_AndOpenDotaDown_IsSourceUnavailable()
    {
        var parser = new FakeParser(() => throw new InvalidOperationException("must not parse"));

        Assert.Equal(RefreshReplayResult.NoReplay,
            await Handler(parser, new FakeAnalyses(), saved: null, new FakeOpenDota(null)).HandleAsync(Match, CancellationToken.None));
        Assert.Equal(RefreshReplayResult.SourceUnavailable,
            await Handler(parser, new FakeAnalyses(), saved: null, new FakeOpenDota(null) { Fail = true }).HandleAsync(Match, CancellationToken.None));
        Assert.Empty(parser.FreshCalls);
    }

    public static TheoryData<string, RefreshReplayResult> Failures => new()
    {
        { "expired", RefreshReplayResult.Expired },
        { "unreadable", RefreshReplayResult.Failed },
        { "busy", RefreshReplayResult.ParserBusy },
        { "unavailable", RefreshReplayResult.ParserUnavailable },
    };

    // The point of this use case: a replay that is gone or unreadable never replaces an analysis that works today
    // (ParseReplayHandler would save "expired" or "failed").
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task AFailure_SavesNothing(string failure, RefreshReplayResult expected)
    {
        var analyses = new FakeAnalyses();
        var parser = new FakeParser(() => throw (failure switch
        {
            "expired" => new ReplayExpiredException("Gone."),
            "unreadable" => new ReplayUnreadableException("Corrupt."),
            "busy" => new ReplayParserBusyException("Busy."),
            _ => (Exception)new ReplayParserUnavailableException("Off."),
        }));

        var result = await Handler(parser, analyses, saved: SavedUrl, new FakeOpenDota(null)).HandleAsync(Match, CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Empty(analyses.Saved);
    }

    [Fact]
    public async Task Prefetch_OnlyDownloads_WithTheSameUrl_AndSavesNothing()
    {
        var parser = new FakeParser(() => throw new InvalidOperationException("must not parse"));
        var analyses = new FakeAnalyses();

        var result = await Handler(parser, analyses, saved: null, new FakeOpenDota(OpenDotaUrl)).PrefetchAsync(Match, CancellationToken.None);
        var gone = await Handler(new FakeParser(() => throw new InvalidOperationException()) { PrefetchFails = new ReplayExpiredException("Gone.") },
            analyses, saved: SavedUrl, new FakeOpenDota(null)).PrefetchAsync(Match, CancellationToken.None);

        Assert.Equal((RefreshReplayResult.Prefetched, RefreshReplayResult.Expired), (result, gone));
        Assert.Equal([(Match, OpenDotaUrl)], parser.Prefetches);
        Assert.Empty(parser.FreshCalls);
        Assert.Empty(analyses.Saved);
    }

    private static RefreshReplayHandler Handler(FakeParser parser, FakeAnalyses analyses, Uri? saved, FakeOpenDota openDota) =>
        new(parser, analyses, new FakeMatches(saved), openDota, new FixedTimeProvider(Now), NullLogger<RefreshReplayHandler>.Instance);

    private sealed class FakeParser(Func<ReplayAnalysis> parse) : IReplayParser
    {
        public List<(MatchId MatchId, Uri ReplayUrl)> FreshCalls { get; } = [];

        public int PlainCalls { get; private set; }

        public Task<ReplayAnalysis> ParseAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken)
        {
            PlainCalls++;
            return Task.FromResult(parse());
        }

        public Task<ReplayAnalysis> ParseFreshAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken)
        {
            FreshCalls.Add((matchId, replayUrl));
            return Task.FromResult(parse());
        }

        public List<(MatchId MatchId, Uri ReplayUrl)> Prefetches { get; } = [];

        public Exception? PrefetchFails { get; init; }

        public Task PrefetchAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken)
        {
            Prefetches.Add((matchId, replayUrl));
            return PrefetchFails is null ? Task.CompletedTask : Task.FromException(PrefetchFails);
        }
    }

    private sealed class FakeAnalyses : IReplayAnalysisRepository
    {
        public List<SavedReplayAnalysis> Saved { get; } = [];

        public Task<SavedReplayAnalysis?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult<SavedReplayAnalysis?>(null);

        public Task SaveAsync(SavedReplayAnalysis result, CancellationToken cancellationToken)
        {
            Saved.Add(result);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMatches(Uri? replayUrl) : IMatchRepository
    {
        public Task<SavedMatch?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult(replayUrl is null ? null : new SavedMatch(MatchWith(replayUrl), Now));

        public Task SaveAsync(MatchDetails match, DateTimeOffset savedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The refresh never saves matches.");
    }

    private sealed class FakeOpenDota(Uri? replayUrl) : IReplayAnalyzer
    {
        public bool Fail { get; init; }

        public int Calls { get; private set; }

        public Task<MatchDetails?> GetMatchDetailsAsync(MatchId matchId, CancellationToken cancellationToken)
        {
            Calls++;
            return Fail
                ? throw new DataSourceUnavailableException("OpenDota is down.")
                : Task.FromResult<MatchDetails?>(replayUrl is null ? null : MatchWith(replayUrl));
        }
    }

    private static MatchDetails MatchWith(Uri replayUrl) =>
        new(Match, TimeSpan.FromSeconds(1725), DateTimeOffset.UnixEpoch, Team.Radiant, 60, 22, IsParsed: false,
            [MatchPlayerBuilder.Create(heroId: 31, team: Team.Radiant)], ReplayUrl: replayUrl);
}
