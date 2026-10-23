using System.Collections.Concurrent;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Infrastructure;
using DotaAnalyst.Infrastructure.ReplayParser;
using DotaAnalyst.Tests.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotaAnalyst.Tests.Infrastructure;

// The real queue, worker and use case, with a fake parser and an in-memory repository.
public sealed class ReplayParseWorkerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri ReplayUrl = new("http://replay202.valve.net/570/1_2.dem.bz2");

    private readonly FakeParser parser = new();
    private readonly InMemoryResults results = new();
    private readonly ServiceProvider services;
    private readonly ReplayParseWorker worker;

    public ReplayParseWorkerTests()
    {
        var collection = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(new FixedTimeProvider(Now))
            .AddSingleton<IReplayParser>(parser)
            .AddSingleton<IReplayAnalysisRepository>(results)
            .AddScoped<ParseReplayHandler>();
        DependencyInjection.AddReplayParseQueue(collection);
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        worker = Assert.IsType<ReplayParseWorker>(Assert.Single(services.GetServices<IHostedService>()));
    }

    private IReplayParseQueue Queue => services.GetRequiredService<IReplayParseQueue>();

    public Task InitializeAsync() => worker.StartAsync(CancellationToken.None);

    public async Task DisposeAsync()
    {
        await worker.StopAsync(CancellationToken.None);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Worker_ParsesAndSaves_ThenTheMatchIsNoLongerQueued()
    {
        var match = ReplayBuilder.MatchIdOf(1);

        Queue.TryEnqueue(match, ReplayUrl);
        await WaitUntilNotQueuedAsync(match);

        var saved = results.Saved[match];
        Assert.Equal(SavedReplayState.Ready, saved.State);
        Assert.Equal(Now, saved.SavedAt);
        Assert.Equal(ReplayUrl, parser.Calls.Single().ReplayUrl);
    }

    [Fact]
    public async Task Worker_ParserUnavailable_SavesNothingAndPausesParsing()
    {
        var match = ReplayBuilder.MatchIdOf(1);
        parser.Fail[match] = new ReplayParserUnavailableException("The machine is off.");

        Queue.TryEnqueue(match, ReplayUrl);
        await WaitUntilNotQueuedAsync(match);

        Assert.Empty(results.Saved);
        Assert.True(services.GetRequiredService<ReplayParserCircuitBreaker>().IsOpen);
        // Meanwhile nothing is queued: the analysis says "unavailable".
        Assert.Equal(ReplayEnqueueResult.Unavailable, Queue.TryEnqueue(ReplayBuilder.MatchIdOf(2), ReplayUrl));
    }

    [Fact]
    public async Task Worker_ParserUnavailable_DropsWhatWasWaitingWithoutCallingTheParser()
    {
        var first = ReplayBuilder.MatchIdOf(1);
        var second = ReplayBuilder.MatchIdOf(2);
        var release = new TaskCompletionSource();
        parser.Gate = release.Task;
        parser.Fail[first] = new ReplayParserUnavailableException("The machine is off.");

        Queue.TryEnqueue(first, ReplayUrl);
        Queue.TryEnqueue(second, ReplayUrl);
        release.SetResult();
        await WaitUntilNotQueuedAsync(second);

        Assert.Equal(first, Assert.Single(parser.Calls).MatchId);
        Assert.Empty(results.Saved);
    }

    [Fact]
    public async Task Worker_UnexpectedError_IsLogged_AndTheNextReplayIsStillParsed()
    {
        var broken = ReplayBuilder.MatchIdOf(1);
        var next = ReplayBuilder.MatchIdOf(2);
        parser.Fail[broken] = new InvalidOperationException("Bug.");

        Queue.TryEnqueue(broken, ReplayUrl);
        await WaitUntilNotQueuedAsync(broken);
        Queue.TryEnqueue(next, ReplayUrl);
        await WaitUntilNotQueuedAsync(next);

        Assert.False(results.Saved.ContainsKey(broken));
        Assert.Equal(SavedReplayState.Ready, results.Saved[next].State);
        Assert.False(services.GetRequiredService<ReplayParserCircuitBreaker>().IsOpen);
    }

    [Fact]
    public async Task Worker_ParserBusy_SavesNothing_DoesNotPauseParsing_AndCanBeQueuedAgain()
    {
        // The parser is up (it answered "busy"): other matches go on, and the next visit queues this one again.
        var match = ReplayBuilder.MatchIdOf(1);
        parser.Fail[match] = new ReplayParserBusyException("Busy.");

        Queue.TryEnqueue(match, ReplayUrl);
        await WaitUntilNotQueuedAsync(match);

        Assert.Empty(results.Saved);
        Assert.False(services.GetRequiredService<ReplayParserCircuitBreaker>().IsOpen);
        Assert.Equal(ReplayEnqueueResult.Enqueued, Queue.TryEnqueue(match, ReplayUrl));
    }

    [Fact]
    public async Task Worker_DownloadFailed_IsSavedAsFailed_WithoutPausingParsing()
    {
        // What the client turns the parser's 502 (Valve's download failed) into.
        var match = ReplayBuilder.MatchIdOf(1);
        parser.Fail[match] = new ReplayUnreadableException("Valve's download failed.");

        Queue.TryEnqueue(match, ReplayUrl);
        await WaitUntilNotQueuedAsync(match);

        Assert.Equal(SavedReplayState.Failed, results.Saved[match].State);
        Assert.False(services.GetRequiredService<ReplayParserCircuitBreaker>().IsOpen);
    }

    [Fact]
    public async Task Worker_ParsedButNotSaved_IsNotParsedAgainRightAway()
    {
        // The page polls every few seconds: without a saved result it would queue the same replay again and again.
        var match = ReplayBuilder.MatchIdOf(1);
        results.Fail = true;

        Queue.TryEnqueue(match, ReplayUrl);
        await WaitUntilNotQueuedAsync(match);

        Assert.Equal(ReplayEnqueueResult.Unavailable, Queue.TryEnqueue(match, ReplayUrl));
        Assert.Single(parser.Calls);
    }

    [Fact]
    public async Task Worker_ExpiredReplay_IsSavedAsExpired()
    {
        var match = ReplayBuilder.MatchIdOf(1);
        parser.Fail[match] = new ReplayExpiredException("Gone.");

        Queue.TryEnqueue(match, ReplayUrl);
        await WaitUntilNotQueuedAsync(match);

        Assert.Equal(SavedReplayState.Expired, results.Saved[match].State);
    }

    private async Task WaitUntilNotQueuedAsync(MatchId match)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Queue.IsQueued(match))
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeParser : IReplayParser
    {
        public ConcurrentDictionary<MatchId, Exception> Fail { get; } = new();

        public ConcurrentQueue<(MatchId MatchId, Uri ReplayUrl)> Calls { get; } = new();

        // Lets a test hold the parser until it has queued everything it needs.
        public Task Gate { get; set; } = Task.CompletedTask;

        public async Task<ReplayAnalysis> ParseAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken)
        {
            Calls.Enqueue((matchId, replayUrl));
            await Gate.WaitAsync(cancellationToken);
            if (Fail.TryGetValue(matchId, out var exception))
            {
                throw exception;
            }

            return ReplayBuilder.Analysis(matchId.Value, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom));
        }
    }

    private sealed class InMemoryResults : IReplayAnalysisRepository
    {
        public ConcurrentDictionary<MatchId, SavedReplayAnalysis> Saved { get; } = new();

        // Saving fails (a database error that doesn't stop the app).
        public bool Fail { get; set; }

        public Task<SavedReplayAnalysis?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult(Saved.GetValueOrDefault(matchId));

        public Task SaveAsync(SavedReplayAnalysis result, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new StorageUnavailableException("Could not save.");
            }

            Saved[result.MatchId] = result;
            return Task.CompletedTask;
        }
    }
}
