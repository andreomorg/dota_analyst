using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Players;
using Microsoft.EntityFrameworkCore;

namespace DotaAnalyst.Infrastructure.Persistence.Players;

public class PlayerSearchRepository(IDbContextFactory<AppDbContext> dbContextFactory, StorageGuard storageGuard) : IPlayerSearchRepository
{
    public Task RecordSearchAsync(AccountId accountId, DateTimeOffset searchedAt, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"recording the search for account {accountId}", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var at = searchedAt.ToUniversalTime();

            // An "upsert" in one statement: inserts the first search, or updates the existing row. Reading
            // the count and writing count + 1 in two steps would lose searches made at the same moment.
            // GREATEST keeps the newest time even if an older request finishes last.
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO player_searches (account_id, first_searched_at, last_searched_at, search_count)
                VALUES ({accountId.Value}, {at}, {at}, 1)
                ON CONFLICT (account_id) DO UPDATE SET
                    last_searched_at = GREATEST(player_searches.last_searched_at, EXCLUDED.last_searched_at),
                    search_count = player_searches.search_count + 1
                """,
                cancellationToken);
        });
}
