using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using Microsoft.EntityFrameworkCore;

namespace DotaAnalyst.Infrastructure.Persistence.Matches;

// Each call opens its own short-lived DbContext (from the factory): a DbContext can't be used by two
// operations at once, and the analysis use case loads the match, heroes and items in parallel.
public class MatchRepository(IDbContextFactory<AppDbContext> dbContextFactory, StorageGuard storageGuard) : IMatchRepository
{
    public Task<SavedMatch?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"reading match {matchId}", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.Matches
                .AsNoTracking()
                .Where(match => match.MatchId == matchId.Value)
                .Select(match => new { match.Details, match.SavedAt })
                .SingleOrDefaultAsync(cancellationToken);

            return row is null ? null : new SavedMatch(StoredMatchMappings.FromJson(row.Details), row.SavedAt);
        });

    public Task SaveAsync(MatchDetails match, DateTimeOffset savedAt, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"saving match {match.MatchId}", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var json = StoredMatchMappings.ToJson(match);

            // One atomic "upsert": inserts a new match or refreshes a saved one, and two requests saving the same
            // match at once can't fail on the duplicate key. The WHERE never lets an unparsed copy replace a parsed
            // one; a parsed copy may replace anything (e.g. a saved row that can no longer be read).
            // The {values} become SQL parameters, so there is no SQL injection.
            // PostgreSQL's timestamptz only takes UTC values from Npgsql, hence ToUniversalTime.
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO matches (match_id, start_time, is_parsed, saved_at, details)
                VALUES ({match.MatchId.Value}, {match.StartTime.ToUniversalTime()}, {match.IsParsed},
                        {savedAt.ToUniversalTime()}, CAST({json} AS jsonb))
                ON CONFLICT (match_id) DO UPDATE SET
                    start_time = EXCLUDED.start_time,
                    is_parsed = EXCLUDED.is_parsed,
                    saved_at = EXCLUDED.saved_at,
                    details = EXCLUDED.details
                WHERE EXCLUDED.is_parsed OR NOT matches.is_parsed
                """,
                cancellationToken);
        });
}
