namespace DotaAnalyst.Application.Abstractions;

// Durable coordination of an external job. Tokens fence writes by workers whose lease expired.
public sealed record OpenDotaParseRequest(
    long MatchId, string State, string? JobId, int SubmitAttempts, int PollAttempts,
    DateTimeOffset RequestedAt, DateTimeOffset UpdatedAt, DateTimeOffset NextAttemptAt,
    DateTimeOffset? LeaseUntil = null, Guid? LeaseToken = null, string? LastErrorCode = null,
    bool EnrichmentOnly = false);

public interface IOpenDotaParseRequestRepository
{
    Task<OpenDotaParseRequest> EnsureAsync(long matchId, DateTimeOffset now, CancellationToken cancellationToken,
        bool enrichmentOnly = false);
    Task<OpenDotaParseRequest?> ClaimAsync(DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken);
    // False if another worker has acquired the task since this worker claimed it.
    Task<bool> CompleteAsync(OpenDotaParseRequest request, CancellationToken cancellationToken);
    Task<bool> CheckpointAsync(OpenDotaParseRequest request, CancellationToken cancellationToken);
}
