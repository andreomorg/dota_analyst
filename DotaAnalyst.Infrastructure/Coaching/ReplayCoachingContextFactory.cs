using System.Text.Json;
using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.ReplayParser;

namespace DotaAnalyst.Infrastructure.Coaching;

// Local raw document -> domain analysis and transient positions -> prepared coaching context.
// Raw identities and position trails never become part of the context returned to the caller.
public static class ReplayCoachingContextFactory
{
    public static CoachingContext Create(string document, MatchId matchId, int heroId, CoachingPatch patch)
    {
        var response = Read(document, matchId);
        return BuildCoachingContext.Build(response.ToReplayAnalysis(matchId), heroId, patch,
            ReplayParserMappings.ReadPositions(response));
    }

    public static CoachingDecisionContext CreateDecision(string document, MatchId matchId, int heroId, CoachingPatch patch)
    {
        var response = Read(document, matchId);
        return BuildCoachingDecisionContext.Build(response.ToReplayAnalysis(matchId), heroId, patch,
            ReplayParserMappings.ReadPositions(response));
    }

    private static ReplayParserMatchResponse Read(string document, MatchId matchId)
    {
        using var parsed = JsonDocument.Parse(document);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object
            || !parsed.RootElement.TryGetProperty("match", out var match) || match.ValueKind != JsonValueKind.Object
            || !match.TryGetProperty("matchId", out var id) || id.ValueKind != JsonValueKind.String
            || id.GetString() != matchId.ToString())
            throw new InvalidDataException("The replay document belongs to a different or unknown match.");
        var response = JsonSerializer.Deserialize<ReplayParserMatchResponse>(document, OllamaCoachingProvider.JsonOptions)
            ?? throw new InvalidDataException("Missing replay document.");
        return response;
    }
}
