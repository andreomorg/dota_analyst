using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// Reads a kept replay document into the domain: the analysis and the positions (ReplayParserMappings). Also reads a
// document handed in (IReplayDocumentReader), the same way.
public sealed class ArchivedReplaySource(IReplayDocumentArchive archive) : IArchivedReplaySource, IReplayDocumentReader
{
    public async Task<ArchivedReplay?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
        await archive.FindAsync(matchId, cancellationToken) is { } document ? ToReplay(document, matchId) : null;

    public (MatchId MatchId, ArchivedReplay Replay) Read(string document)
    {
        string? text;
        try
        {
            using var parsed = JsonDocument.Parse(document);
            text = parsed.RootElement.ValueKind == JsonValueKind.Object && parsed.RootElement.TryGetProperty("match", out var match)
                && match.ValueKind == JsonValueKind.Object && match.TryGetProperty("matchId", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString() : null;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The replay document is not JSON.", ex);
        }

        if (!long.TryParse(text, out var number) || !MatchId.TryCreate(number, out var matchId))
        {
            throw new InvalidDataException("The replay document has no match ID.");
        }

        return (matchId, ToReplay(document, matchId));
    }

    private static ArchivedReplay ToReplay(string document, MatchId matchId)
    {
        var response = Read(document, matchId);
        var mapped = response.ToMappedReplay(matchId);
        return new ArchivedReplay(mapped.Analysis, mapped.Positions,
            ReplayParserMappings.ReadActivity(response), StartedAt(document), ReplayParserMappings.ReadMetadata(response),
            ReplayParserMappings.ReadFacts(response));
    }

    // match.startedAt: Unix seconds of the horn (null when the replay does not say).
    internal static DateTimeOffset? StartedAt(string document)
    {
        using var parsed = JsonDocument.Parse(document);
        return parsed.RootElement.TryGetProperty("match", out var match) && match.TryGetProperty("startedAt", out var started)
            && started.ValueKind == JsonValueKind.Number && started.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
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
