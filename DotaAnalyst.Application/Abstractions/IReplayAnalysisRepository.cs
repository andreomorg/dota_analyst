using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Abstractions;

public enum SavedReplayState
{
    // Parsed: Analysis has the laning phase. Final.
    Ready,
    // The parser could not read the replay: tried again after GetMatchAnalysisHandler.FailedReplayRetryInterval.
    Failed,
    // Valve no longer has the replay. Final, except when it was saved less than ReplayAvailability.UploadWindow
    // after the match ended: then it may only not have been uploaded yet, and it counts as Failed.
    Expired,
}

// The saved result of parsing a match's replay. Only created through the factories below, so Analysis is
// always there for Ready and never for the other states.
public sealed record SavedReplayAnalysis
{
    private SavedReplayAnalysis(MatchId matchId, SavedReplayState state, ReplayAnalysis? analysis, DateTimeOffset savedAt)
    {
        MatchId = matchId;
        State = state;
        Analysis = analysis;
        SavedAt = savedAt;
    }

    public MatchId MatchId { get; }

    public SavedReplayState State { get; }

    // Only for Ready.
    public ReplayAnalysis? Analysis { get; }

    // When the result was saved, from the app's clock. For Failed, the retry is measured from it.
    public DateTimeOffset SavedAt { get; }

    public static SavedReplayAnalysis Ready(ReplayAnalysis analysis, DateTimeOffset savedAt) =>
        new(analysis.MatchId, SavedReplayState.Ready, analysis, savedAt);

    public static SavedReplayAnalysis Failed(MatchId matchId, DateTimeOffset savedAt) =>
        new(matchId, SavedReplayState.Failed, null, savedAt);

    public static SavedReplayAnalysis Expired(MatchId matchId, DateTimeOffset savedAt) =>
        new(matchId, SavedReplayState.Expired, null, savedAt);
}

// Results of replay parsing saved in our own database, one per match. Only definitive answers are saved (see
// IReplayParser); "the parser is unavailable" never is. A match being parsed right now has no row yet: that
// state lives in IReplayParseQueue.
public interface IReplayAnalysisRepository
{
    /// <returns>The saved result, or null when the match's replay was never parsed (or the parse didn't finish).</returns>
    /// <exception cref="StorageUnavailableException">The database could not be read.</exception>
    Task<SavedReplayAnalysis?> FindAsync(MatchId matchId, CancellationToken cancellationToken);

    // Saves the result, replacing any earlier one of the same match (a failure tried again may become ready).
    // Saving the same match twice at once is not an error.
    /// <exception cref="StorageUnavailableException">The database could not be written.</exception>
    Task SaveAsync(SavedReplayAnalysis result, CancellationToken cancellationToken);
}
