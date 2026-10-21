using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Abstractions;

// A replay kept whole on this machine (replay_documents, only where documents are kept): the analysis, the positions
// it was computed from and what each hero used. Positions and activity are never saved with the analysis; only the
// kept document has them. Activity is null for documents without it.
public sealed record ArchivedReplay(ReplayAnalysis Analysis, IReadOnlyList<HeroPositions> Positions, ReplayActivity? Activity = null);

public interface IArchivedReplaySource
{
    // Null when the match has no kept document (or there is no storage). Throws InvalidDataException when the document
    // is unreadable or belongs to another match.
    Task<ArchivedReplay?> FindAsync(MatchId matchId, CancellationToken cancellationToken);
}
