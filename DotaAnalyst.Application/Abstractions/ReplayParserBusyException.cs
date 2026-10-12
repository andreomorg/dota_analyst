namespace DotaAnalyst.Application.Abstractions;

// Thrown by IReplayParser when the parser answered, but was still busy with other replays after waiting for it
// a few times (it parses only a few at a time, and is shared with the project owner's local API). It is up and
// working, so parsing does not pause: nothing is saved and the match is queued again on a later visit.
public class ReplayParserBusyException(string message, Exception? innerException = null)
    : Exception(message, innerException);
