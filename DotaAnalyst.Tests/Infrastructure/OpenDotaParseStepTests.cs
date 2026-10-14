using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.OpenDota;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Tests.Infrastructure;

public sealed class OpenDotaParseStepTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task NewRequest_CheckpointsBeforePost_AndKeepsJob()
    {
        var repo = new Requests(); var client = new Client(repo);
        await Step(repo, client).RunAsync(default);
        Assert.Equal("submitting", repo.Checkpoint!.State);
        Assert.Equal(1, repo.Checkpoint.SubmitAttempts);
        Assert.Equal("123", repo.Row.JobId);
        Assert.Equal("processing", repo.Row.State);
    }

    [Theory]
    [InlineData("submitting", null)]
    [InlineData("refreshing", null)]
    [InlineData("processing", "123")]
    public async Task RecoveryOrExistingJob_DoesNotPost(string state, string? job)
    {
        var repo = new Requests { Row = Request() with { State = state, JobId = job } };
        var client = new Client(repo);
        await Step(repo, client).RunAsync(default);
        Assert.Equal(0, client.Posts);
    }

    [Fact]
    public async Task UnknownPost_IsReconciled_AndAttemptsAreBounded()
    {
        var repo = new Requests(); var client = new Client(repo) { Unknown = true };
        await Step(repo, client).RunAsync(default);
        Assert.Equal("refreshing", repo.Row.State);
        Assert.Equal(1, repo.Row.SubmitAttempts);
        repo.Row = Request() with { SubmitAttempts = 3 };
        await Step(repo, client).RunAsync(default);
        Assert.Equal(1, client.Posts);
        Assert.Equal("attemptLimit", repo.Row.LastErrorCode);
    }

    [Fact]
    public async Task ParsedAndUnfinishedMatches_DoNotRegisterRequests()
    {
        var repo = new Requests(); var coordinator = new OpenDotaParseCoordinator(repo,
            Options.Create(new OpenDotaParsingOptions { Enabled = true }), TimeProvider.System);
        MatchId.TryCreate(1, out var id);
        var match = new MatchDetails(id, TimeSpan.Zero, Now.AddDays(-1), Team.Radiant, 0, 0, true, [], SupplementalStatsVersion: SupplementalStatsSchema.CurrentVersion);
        Assert.Equal("ready", (await coordinator.EnsureAsync(match, default)).Status);
        Assert.Equal("matchNotFinished", (await coordinator.EnsureAsync(match with { IsParsed = false, WinningTeam = null }, default)).Reason);
        Assert.Equal(0, repo.Ensures);
        Assert.Equal("queued", (await coordinator.EnsureAsync(match with { IsParsed = false }, default)).Status);
        Assert.Equal(1, repo.Ensures);
    }

    private static OpenDotaParseStep Step(Requests repo, Client client) =>
        new(repo, client, new Analyzer(), new Matches(), Options.Create(new OpenDotaParsingOptions()), TimeProvider.System);
    [Fact]
    public async Task EnrichmentOnly_NeverSubmitsParseEvenWithoutExternalParsedData()
    {
        var repo = new Requests { Row = Request() with { EnrichmentOnly = true, State = "refreshing" } };
        var client = new Client(repo);
        await Step(repo, client).RunAsync(default);
        Assert.Equal(0, client.Posts);
        Assert.Equal("refreshing", repo.Row.State);
        Assert.True(repo.Row.EnrichmentOnly);
    }

    [Fact]
    public async Task CancelledPost_KeepsDurableIntent_AndRecoveryDoesNotPostAgain()
    {
        var repo = new Requests(); var client = new Client(repo) { Cancel = true };
        await Assert.ThrowsAsync<OperationCanceledException>(() => Step(repo, client).RunAsync(default));
        Assert.Equal("submitting", repo.Row.State);
        client.Cancel = false;
        await Step(repo, client).RunAsync(default);
        Assert.Equal(1, client.Posts);
        Assert.Equal("refreshing", repo.Row.State);
    }
    [Fact]
    public async Task Completion_RefreshesAndPersistsBeforeReady_StorageFailureKeepsJob()
    {
        var repo = new Requests { Row = Request() with { State = "refreshing", JobId = "123" } };
        var store = new Matches { Fail = true };
        var analyzer = new ParsedAnalyzer();
        var client = new Client(repo);
        var step = new OpenDotaParseStep(repo, client, analyzer, store,
            Options.Create(new OpenDotaParsingOptions()), TimeProvider.System);
        await Assert.ThrowsAsync<StorageUnavailableException>(() => step.RunAsync(default));
        Assert.Equal("refreshing", repo.Row.State);
        Assert.Equal("123", repo.Row.JobId);
        store.Fail = false;
        await step.RunAsync(default);
        Assert.Equal("ready", repo.Row.State);
        Assert.Equal(0, client.Posts);
        Assert.Equal(2, analyzer.Refreshes);
    }

    private sealed class ParsedAnalyzer : IReplayAnalyzer
    {
        public int Refreshes;
        public Task<MatchDetails?> GetMatchDetailsAsync(MatchId id, CancellationToken ct) => throw new InvalidOperationException("Must refresh");
        public Task<MatchDetails?> RefreshMatchDetailsAsync(MatchId id, CancellationToken ct)
        {
            Refreshes++;
            return Task.FromResult<MatchDetails?>(new(id, TimeSpan.Zero, Now.AddDays(-1), Team.Radiant, 0, 0, true, []));
        }
    }
    private static OpenDotaParseRequest Request() => new(1, "queued", null, 0, 0, Now, Now, Now,
        Now.AddMinutes(2), Guid.NewGuid());
    private sealed class Requests : IOpenDotaParseRequestRepository
    {
        public OpenDotaParseRequest Row = Request();
        public OpenDotaParseRequest? Checkpoint;
        public int Ensures;
        public Task<OpenDotaParseRequest> EnsureAsync(long id, DateTimeOffset now, CancellationToken ct, bool enrichmentOnly = false)
        { Ensures++; Row = Row with { EnrichmentOnly = enrichmentOnly }; return Task.FromResult(Row); }
        public Task<OpenDotaParseRequest?> ClaimAsync(DateTimeOffset now, TimeSpan lease, CancellationToken ct) => Task.FromResult<OpenDotaParseRequest?>(Row);
        public Task<bool> CompleteAsync(OpenDotaParseRequest row, CancellationToken ct) { Row = row; return Task.FromResult(true); }
        public Task<bool> CheckpointAsync(OpenDotaParseRequest row, CancellationToken ct) { Checkpoint = Row = row; return Task.FromResult(true); }
    }
    private sealed class Client(Requests repo) : IOpenDotaParseClient
    {
        public int Posts; public bool Unknown; public bool Cancel;
        public Task<string> SubmitAsync(MatchId id, CancellationToken ct)
        {
            Assert.Equal("submitting", repo.Row.State); Posts++;
            if (Cancel) throw new OperationCanceledException();
            if (Unknown) throw new OpenDotaParseException("timeout", true);
            return Task.FromResult("123");
        }
        public Task<bool> IsPendingAsync(string id, CancellationToken ct) => Task.FromResult(false);
    }
    private sealed class Analyzer : IReplayAnalyzer
    {
        public Task<MatchDetails?> GetMatchDetailsAsync(MatchId id, CancellationToken ct) => Task.FromResult<MatchDetails?>(null);
    }
    private sealed class Matches : IMatchRepository
    {
        public bool Fail;
        public Task<SavedMatch?> FindAsync(MatchId id, CancellationToken ct) => Task.FromResult<SavedMatch?>(null);
        public Task SaveAsync(MatchDetails match, DateTimeOffset at, CancellationToken ct) =>
            Fail ? throw new StorageUnavailableException("test") : Task.CompletedTask;
    }
}
