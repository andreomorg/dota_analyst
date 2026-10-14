namespace DotaAnalyst.Application.Matches;

/// <summary>OpenDota processing, independent of our Go parser's replayStatus. Ready requires verified parsed match data.</summary>
/// <param name="Status">queued, processing, refreshing, ready, deferred, failed or unavailable.</param>
/// <param name="Reason">Sanitized reason code, never an external error message.</param>
/// <param name="RetryAfterSeconds">Suggested next analysis refresh; not an estimated processing duration.</param>
/// <param name="RequestedAt">When our durable request was first registered (UTC), or null without a request.</param>
/// <param name="EnrichmentOnly">True when fetching additional fields of an already parsed match; no new parse POST.</param>
public sealed record OpenDotaProcessing(
    string Status, string? Reason = null, int? RetryAfterSeconds = null, DateTimeOffset? RequestedAt = null,
    bool EnrichmentOnly = false);
