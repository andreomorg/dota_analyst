using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// Reads a kept replay document into the domain: the analysis and the positions (ReplayParserMappings).
public sealed class ArchivedReplaySource(IReplayDocumentArchive archive) : IArchivedReplaySource
{
    public async Task<ArchivedReplay?> FindAsync(MatchId matchId, CancellationToken cancellationToken)
    {
        if (await archive.FindAsync(matchId, cancellationToken) is not { } document)
        {
            return null;
        }

        var response = Read(document, matchId);
        return new ArchivedReplay(response.ToReplayAnalysis(matchId), ReplayParserMappings.ReadPositions(response),
            ReplayParserMappings.ReadActivity(response));
    }

    // The document must be the one of this match: a wrong row would put another match's numbers under this ID.
    internal static ReplayParserMatchResponse Read(string document, MatchId matchId)
    {
        using var parsed = JsonDocument.Parse(document);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object
            || !parsed.RootElement.TryGetProperty("match", out var match) || match.ValueKind != JsonValueKind.Object
            || !match.TryGetProperty("matchId", out var id) || id.ValueKind != JsonValueKind.String
            || id.GetString() != matchId.ToString())
            throw new InvalidDataException("The replay document belongs to a different or unknown match.");
        return JsonSerializer.Deserialize<ReplayParserMatchResponse>(document, CoachingJson.Options)
            ?? throw new InvalidDataException("Missing replay document.");
    }
}
