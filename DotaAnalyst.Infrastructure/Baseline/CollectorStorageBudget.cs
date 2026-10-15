using DotaAnalyst.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DotaAnalyst.Infrastructure.Baseline;

public sealed record CollectorStorageLimits(long MaxDatabaseBytes);

// Infrastructure resource limit, not a Dota rule. Measures the local database, never the production mirror.
public sealed class CollectorStorageBudget(
    IDbContextFactory<AppDbContext> factory, StorageGuard guard, CollectorStorageLimits limits)
{
    public async Task<bool> CanCollectAsync(CancellationToken cancellationToken)
    {
        if (limits.MaxDatabaseBytes <= 0) return true;
        return await guard.RunAsync("checking the local collector storage budget", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var bytes = await db.Database.SqlQuery<long>($"""
                SELECT pg_database_size(current_database()) AS "Value"
                """)
                .SingleAsync(cancellationToken);
            return bytes < limits.MaxDatabaseBytes;
        });
    }
}
