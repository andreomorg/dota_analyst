using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.Persistence.Replays;

// Same rules as the other repositories: one short-lived DbContext per operation, everything through StorageGuard.
public class ReplayAnalysisRepository(
    IDbContextFactory<AppDbContext> dbContextFactory,
    StorageGuard storageGuard,
    ILogger<ReplayAnalysisRepository> logger) : IReplayAnalysisRepository
{
    public Task<SavedReplayAnalysis?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"reading the replay analysis of match {matchId}", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.ReplayAnalyses
                .AsNoTracking()
                .SingleOrDefaultAsync(analysis => analysis.MatchId == matchId.Value, cancellationToken);
            if (row is null)
            {
                return null;
            }

            try
            {
                return StoredReplayMappings.ToDomain(row.MatchId, row.Status, row.Data, row.SavedAt);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                // A row that can't be read anymore (format changed, damaged data) counts as "never parsed": the
                // replay is parsed again and the new result replaces the row. Not a database failure, so the
                // analysis doesn't say "unavailable" because of it.
                logger.LogWarning(ex, "The saved replay analysis of match {MatchId} can't be read; it will be parsed again", matchId);
                return null;
            }
        });

    public Task SaveAsync(SavedReplayAnalysis result, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"saving the replay analysis of match {result.MatchId}", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var status = StoredReplayMappings.ToStatus(result.State);
            var json = StoredReplayMappings.ToJson(result);

            // One atomic "upsert": inserts the result or replaces the earlier one (a failure tried again may become
            // ready), and two saves of the same match at once can't fail on the duplicate key. The {values} become
            // SQL parameters (no SQL injection); timestamptz only takes UTC values from Npgsql.
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO replay_analyses (match_id, status, data, saved_at)
                VALUES ({result.MatchId.Value}, {status}, CAST({json} AS jsonb), {result.SavedAt.ToUniversalTime()})
                ON CONFLICT (match_id) DO UPDATE SET
                    status = EXCLUDED.status,
                    data = EXCLUDED.data,
                    saved_at = EXCLUDED.saved_at
                """,
                cancellationToken);
        });
}
