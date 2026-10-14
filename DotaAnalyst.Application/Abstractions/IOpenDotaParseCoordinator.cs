using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Abstractions;

public interface IOpenDotaParseCoordinator
{
    Task<OpenDotaProcessing> EnsureAsync(MatchDetails match, CancellationToken cancellationToken);
}

public interface IOpenDotaParseClient
{
    Task<string> SubmitAsync(MatchId matchId, CancellationToken cancellationToken);
    // False means the job left the external queue, not necessarily that parsing succeeded.
    Task<bool> IsPendingAsync(string jobId, CancellationToken cancellationToken);
}

public sealed class OpenDotaParseException(string code, bool outcomeUnknown = false, Exception? inner = null)
    : Exception(code, inner)
{
    public string Code { get; } = code;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}
