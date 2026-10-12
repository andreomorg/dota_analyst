using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.ReplayParser;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.Persistence.Replays;

// Same rules as the repositories: one short-lived DbContext per operation, everything through StorageGuard.
public class ReplayDocumentArchive(
    IDbContextFactory<AppDbContext> dbContextFactory,
    StorageGuard storageGuard,
    TimeProvider timeProvider,
    ILogger<ReplayDocumentArchive> logger) : IReplayDocumentArchive
{
    public async Task KeepAsync(MatchId matchId, int schemaVersion, string document, CancellationToken cancellationToken)
    {
        try
        {
            await storageGuard.RunAsync($"keeping the replay document of match {matchId}", async () =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var savedAt = timeProvider.GetUtcNow().ToUniversalTime();

                // Atomic upsert, like the other tables; the {values} become SQL parameters.
                await db.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO replay_documents (match_id, schema_version, document, saved_at)
                    VALUES ({matchId.Value}, {schemaVersion}, CAST({document} AS jsonb), {savedAt})
                    ON CONFLICT (match_id) DO UPDATE SET
                        schema_version = EXCLUDED.schema_version,
                        document = EXCLUDED.document,
                        saved_at = EXCLUDED.saved_at
                    """,
                    cancellationToken);
            });
        }
        catch (StorageUnavailableException ex)
        {
            // The analysis itself is saved by ParseReplayHandler; only the document for later analyses is missing.
            logger.LogWarning(ex, "Could not keep the replay document of match {MatchId}", matchId);
        }
    }
}
