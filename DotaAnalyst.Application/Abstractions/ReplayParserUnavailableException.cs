namespace DotaAnalyst.Application.Abstractions;

// Thrown by IReplayParser when the parser could not do the job: not configured, down (the machine that runs it
// is off), too slow, rejecting our credentials, or something else answered in its place (e.g. the tunnel's error
// page). Says nothing about the replay, so nothing is saved, and replay parsing pauses for a while (the parser
// probably won't answer the next one either). Being busy is ReplayParserBusyException instead.
public class ReplayParserUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
