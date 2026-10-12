namespace DotaAnalyst.Application.Abstractions;

// Thrown by IReplayParser when Valve no longer has the replay (replays expire after a few weeks). Final: the
// result is saved as expired and the replay is never asked for again.
public class ReplayExpiredException(string message, Exception? innerException = null)
    : Exception(message, innerException);
