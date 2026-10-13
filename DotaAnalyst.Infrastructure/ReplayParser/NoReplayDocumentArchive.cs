using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// ReplayParser:KeepDocuments off (production): the raw document is dropped, only the refined analysis is saved.
internal sealed class NoReplayDocumentArchive : IReplayDocumentArchive
{
    public Task KeepAsync(MatchId matchId, int schemaVersion, string document, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
