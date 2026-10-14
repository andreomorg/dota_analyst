using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Matches;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.OpenDota;

public sealed class OpenDotaParseCoordinator(IOpenDotaParseRequestRepository repository,
    IOptions<OpenDotaParsingOptions> options, TimeProvider clock) : IOpenDotaParseCoordinator
{
    public async Task<OpenDotaProcessing> EnsureAsync(MatchDetails match, CancellationToken cancellationToken)
    {
        var needsEnrichment = match.IsParsed && match.SupplementalStatsVersion < SupplementalStatsSchema.CurrentVersion;
        if (match.IsParsed && (!needsEnrichment || !options.Value.Enabled)) return new("ready");
        if (!options.Value.Enabled) return new("unavailable", "disabled");
        if (match.WinningTeam is null || match.EndedAt > clock.GetUtcNow()) return new("unavailable", "matchNotFinished");
        try
        {
            var request = await repository.EnsureAsync(match.MatchId.Value, clock.GetUtcNow(), cancellationToken, needsEnrichment);
            // Do not claim external acceptance until a job ID has actually been persisted.
            var state = request.State == "submitting" ? "queued" : request.State;
            var seconds = Math.Clamp((int)Math.Ceiling((request.NextAttemptAt - clock.GetUtcNow()).TotalSeconds),
                options.Value.PollSeconds, options.Value.MaxPollSeconds);
            return new(state, request.LastErrorCode, state is "ready" or "failed" or "unavailable" ? null : seconds,
                request.RequestedAt, needsEnrichment);
        }
        catch (StorageUnavailableException) { return new("unavailable", "storageUnavailable"); }
    }
}

public sealed class DisabledOpenDotaParseCoordinator : IOpenDotaParseCoordinator
{
    public Task<OpenDotaProcessing> EnsureAsync(MatchDetails match, CancellationToken cancellationToken) =>
        Task.FromResult(new OpenDotaProcessing(match.IsParsed ? "ready" : "unavailable",
            match.IsParsed ? null : "storageUnavailable"));
}
