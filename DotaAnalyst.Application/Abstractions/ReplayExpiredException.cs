namespace DotaAnalyst.Application.Abstractions;

// Thrown by IReplayParser when Valve doesn't have the replay. Replays expire after a few weeks, so this is
// usually final: saved as expired and never asked for again. Right after the match it may only mean the replay
// wasn't uploaded yet (see Domain.Replays.ReplayAvailability), and then it is tried again like a failure.
public class ReplayExpiredException(string message, Exception? innerException = null)
    : Exception(message, innerException);
