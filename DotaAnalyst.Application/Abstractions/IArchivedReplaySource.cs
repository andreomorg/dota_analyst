using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Abstractions;

// A replay kept whole on this machine (replay_documents, only where documents are kept): the analysis, the positions
// it was computed from and what each hero used. Positions and activity are never saved with the analysis; only the
// kept document has them. Activity is null for documents without it.
// StartedAt: when the horn sounded (the document's match.startedAt), to pick the game build the match was played on;
// null when the replay does not say.
public sealed record ArchivedReplay(ReplayAnalysis Analysis, IReadOnlyList<HeroPositions> Positions, ReplayActivity? Activity = null,
    DateTimeOffset? StartedAt = null, ReplayMetadata? Metadata = null, ReplayAdditionalFacts? Facts = null);

public interface IArchivedReplaySource
{
    // Null when the match has no kept document (or there is no storage). Throws InvalidDataException when the document
    // is unreadable or belongs to another match.
    Task<ArchivedReplay?> FindAsync(MatchId matchId, CancellationToken cancellationToken);
}
