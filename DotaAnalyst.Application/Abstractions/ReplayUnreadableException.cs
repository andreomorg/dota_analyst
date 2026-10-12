namespace DotaAnalyst.Application.Abstractions;

// Thrown by IReplayParser when the parser answered but could not analyze this replay this time: the file is
// corrupt or in a format it doesn't know, it could not be downloaded from Valve, or the parser refused the
// request (e.g. an address it doesn't accept). The answer is about this replay, not about the parser being
// down, so it is saved as failed and tried again after a while (a newer parser may read it, Valve may answer).
public class ReplayUnreadableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
