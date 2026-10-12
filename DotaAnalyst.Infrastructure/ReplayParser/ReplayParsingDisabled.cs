using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// Replay parsing turned off: nothing is ever queued, parsed or saved, and every analysis of an unparsed match
// says replayStatus "unavailable". The rest of the site works exactly as before the feature existed.

public sealed class NoReplayParser : IReplayParser
{
    public Task<ReplayAnalysis> ParseAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken) =>
        throw new ReplayParserUnavailableException("Replay parsing is not configured.");
}

public sealed class DisabledReplayParseQueue : IReplayParseQueue
{
    public ReplayEnqueueResult TryEnqueue(MatchId matchId, Uri replayUrl) => ReplayEnqueueResult.Unavailable;

    public bool IsQueued(MatchId matchId) => false;
}

public sealed class NoReplayAnalyses : IReplayAnalysisRepository
{
    public Task<SavedReplayAnalysis?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
        Task.FromResult<SavedReplayAnalysis?>(null);

    public Task SaveAsync(SavedReplayAnalysis result, CancellationToken cancellationToken) => Task.CompletedTask;
}
