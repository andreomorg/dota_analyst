using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// Where ReplayParserClient keeps the parser's whole document of a match. Infrastructure only: the document is the
// parser's external format, which the Application never sees.
public interface IReplayDocumentArchive
{
    // Never throws for storage problems (they are logged): keeping the document must not fail the parse, whose
    // analysis is saved anyway. Replaces an earlier document of the same match.
    Task KeepAsync(MatchId matchId, int schemaVersion, string document, CancellationToken cancellationToken);
}
