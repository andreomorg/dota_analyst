using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Abstractions;

// Reads a match replay (today the Go service, implemented in Infrastructure). Slow (downloads tens of MB and
// reads them), so it is only called by the background worker, never while a visitor waits.
//
// The four exceptions tell the caller what to do next:
// - ReplayExpiredException: saved as expired (final, unless too soon after the match: see ReplayAvailability);
// - ReplayUnreadableException: saved as failed and tried again after a while;
// - ReplayParserBusyException: nothing is saved; the match is queued again on a later visit;
// - ReplayParserUnavailableException: nothing is saved and parsing pauses for a while (ReplayParserCircuitBreaker).
public interface IReplayParser
{
    // replayUrl is MatchDetails.ReplayUrl; matchId only goes into the result.
    /// <returns>The laning phase of every hero of the match.</returns>
    /// <exception cref="ReplayExpiredException">Valve doesn't have the replay.</exception>
    /// <exception cref="ReplayUnreadableException">The parser could not download or read this replay, or refused the request.</exception>
    /// <exception cref="ReplayParserBusyException">The parser answered, but stayed busy with other replays.</exception>
    /// <exception cref="ReplayParserUnavailableException">The parser could not be reached, or something else answered.</exception>
    Task<ReplayAnalysis> ParseAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken);
}
