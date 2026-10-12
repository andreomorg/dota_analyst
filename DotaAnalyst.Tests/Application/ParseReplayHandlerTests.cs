using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Tests.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Application;

public class ParseReplayHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly MatchId Match = ReplayBuilder.MatchIdOf(9034320024);
    private static readonly Uri ReplayUrl = new("http://replay202.valve.net/570/9034320024_96247495.dem.bz2");

    [Fact]
    public async Task HandleAsync_Parsed_SavesReady()
    {
        var replay = ReplayBuilder.Analysis(Match.Value, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom));
        var parser = new FakeReplayParser(() => replay);
        var repository = new FakeReplayAnalysisRepository();

        var result = await CreateHandler(parser, repository).HandleAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(ParseReplayResult.Ready, result);
        Assert.Equal((Match, ReplayUrl), Assert.Single(parser.Calls));
        var saved = Assert.Single(repository.Saved);
        Assert.Equal(SavedReplayState.Ready, saved.State);
        Assert.Same(replay, saved.Analysis);
        Assert.Equal(Now, saved.SavedAt);
    }

    [Fact]
    public async Task HandleAsync_Expired_SavesExpired()
    {
        var repository = new FakeReplayAnalysisRepository();
        var parser = new FakeReplayParser(() => throw new ReplayExpiredException("Gone."));

        var result = await CreateHandler(parser, repository).HandleAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(ParseReplayResult.Expired, result);
        var saved = Assert.Single(repository.Saved);
        Assert.Equal(SavedReplayState.Expired, saved.State);
        Assert.Equal(Match, saved.MatchId);
        Assert.Null(saved.Analysis);
    }

    [Fact]
    public async Task HandleAsync_Unreadable_SavesFailed()
    {
        var repository = new FakeReplayAnalysisRepository();
        var parser = new FakeReplayParser(() => throw new ReplayUnreadableException("Corrupt."));

        var result = await CreateHandler(parser, repository).HandleAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(ParseReplayResult.Failed, result);
        var saved = Assert.Single(repository.Saved);
        Assert.Equal(SavedReplayState.Failed, saved.State);
        Assert.Equal(Now, saved.SavedAt);
    }

    [Fact]
    public async Task HandleAsync_ParserUnavailable_SavesNothing()
    {
        var repository = new FakeReplayAnalysisRepository();
        var parser = new FakeReplayParser(() => throw new ReplayParserUnavailableException("Machine is off."));

        var result = await CreateHandler(parser, repository).HandleAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(ParseReplayResult.ParserUnavailable, result);
        Assert.Empty(repository.Saved);
    }

    [Fact]
    public async Task HandleAsync_ParserBusy_SavesNothing()
    {
        var repository = new FakeReplayAnalysisRepository();
        var parser = new FakeReplayParser(() => throw new ReplayParserBusyException("Busy."));

        var result = await CreateHandler(parser, repository).HandleAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(ParseReplayResult.ParserBusy, result);
        Assert.Empty(repository.Saved);
    }

    [Fact]
    public async Task HandleAsync_DatabaseFails_StillReportsTheResult()
    {
        var replay = ReplayBuilder.Analysis(Match.Value, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom));
        var repository = new FakeReplayAnalysisRepository { Fail = true };

        var result = await CreateHandler(new FakeReplayParser(() => replay), repository)
            .HandleAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(ParseReplayResult.Ready, result);
    }

    private static ParseReplayHandler CreateHandler(FakeReplayParser parser, FakeReplayAnalysisRepository repository) =>
        new(parser, repository, new FixedTimeProvider(Now), NullLogger<ParseReplayHandler>.Instance);

    private sealed class FakeReplayParser(Func<ReplayAnalysis> parse) : IReplayParser
    {
        public List<(MatchId MatchId, Uri ReplayUrl)> Calls { get; } = [];

        public Task<ReplayAnalysis> ParseAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken)
        {
            Calls.Add((matchId, replayUrl));
            return Task.FromResult(parse());
        }
    }

    private sealed class FakeReplayAnalysisRepository : IReplayAnalysisRepository
    {
        public bool Fail { get; init; }

        public List<SavedReplayAnalysis> Saved { get; } = [];

        public Task<SavedReplayAnalysis?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult(Saved.LastOrDefault(saved => saved.MatchId == matchId));

        public Task SaveAsync(SavedReplayAnalysis result, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new StorageUnavailableException("The database is down.");
            }

            Saved.Add(result);
            return Task.CompletedTask;
        }
    }
}
