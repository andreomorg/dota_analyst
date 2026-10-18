using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.ReplayParser;
using System.Text.Json;

namespace DotaAnalyst.Infrastructure.Coaching;

public sealed class StoredCoachingDecisionSource(IReplayDocumentArchive archive, IMatchRepository matches) : ICoachingDecisionSource
{
    public async Task<CoachingDecisionContext?> FindAsync(MatchId matchId, int heroId, CancellationToken cancellationToken)
    {
        var document = await archive.FindAsync(matchId, cancellationToken);
        if (document is null) return null;
        using (var raw = JsonDocument.Parse(document))
            if (!raw.RootElement.GetProperty("players").EnumerateArray().Any(p => p.TryGetProperty("heroId", out var id) && id.GetInt32() == heroId))
                return null;
        string? patchId = null;
        try { patchId = (await matches.FindAsync(matchId, cancellationToken))?.Match.PatchId?.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        catch (StorageUnavailableException) { /* The replay remains usable without patch metadata. */ }
        var patch = new CoachingPatch(patchId, patchId is null ? "unknown:stored-metadata-unavailable" : "stored-match:patch-id", null, false);
        var context = ReplayCoachingContextFactory.CreateDecision(document, matchId, heroId, patch);
        return context with { Scenes = context.Scenes.Take(3).ToArray() };
    }
}
