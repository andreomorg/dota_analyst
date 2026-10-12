using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Abstractions;

public enum ReplayEnqueueResult
{
    // Added: it will be parsed in the background.
    Enqueued,
    // Already waiting or being parsed: nothing to do.
    AlreadyQueued,
    // Too many replays waiting: not added (a later visit tries again).
    Full,
    // Replay parsing is off (not configured, or results could not be saved) or temporarily down (the parser
    // stopped answering recently): not added. A later visit tries again.
    Unavailable,
}

// Replays waiting to be parsed in the background (implemented in Infrastructure, with the worker that empties
// it and calls ParseReplayHandler). It is also the only place that knows whether replay parsing is available
// right now: the analysis asks by trying to enqueue, so a feature that is off or down simply answers Unavailable.
public interface IReplayParseQueue
{
    // Never waits: answers at once whether the match was added.
    ReplayEnqueueResult TryEnqueue(MatchId matchId, Uri replayUrl);

    // True from TryEnqueue until the parse result is saved (or the parse is given up), so a visit in between
    // never queues the same match twice nor sees "no result and not queued".
    bool IsQueued(MatchId matchId);
}
