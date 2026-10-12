namespace DotaAnalyst.Application.Abstractions;

// Thrown by IReplayParser when the parser itself could not do the job: not configured, down (the machine that
// runs it is off), busy, too slow, rejecting our credentials, or something else answered in its place (e.g. the
// tunnel's error page). Says nothing about the replay, so nothing is saved: the match is queued again later.
public class ReplayParserUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
