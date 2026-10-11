using Microsoft.EntityFrameworkCore;

namespace DotaAnalyst.Infrastructure.Persistence.Catalogs;

// A saved catalog: ID -> name, and when it was loaded from OpenDota.
public sealed record SavedCatalog(IReadOnlyDictionary<int, string> Names, DateTimeOffset UpdatedAt);

// Saved copies of the hero/item catalogs. An Infrastructure detail of the catalogs (the Application only
// knows IHeroCatalog/IItemCatalog), behind an interface so the catalog logic can be tested without a database.
public interface ICatalogStore
{
    /// <returns>The saved catalog, or null when it was never saved.</returns>
    /// <exception cref="Application.Abstractions.StorageUnavailableException">The database could not be read.</exception>
    Task<SavedCatalog?> LoadAsync(string catalog, CancellationToken cancellationToken);

    /// <exception cref="Application.Abstractions.StorageUnavailableException">The database could not be written.</exception>
    Task ReplaceAsync(string catalog, IReadOnlyDictionary<int, string> names, DateTimeOffset updatedAt, CancellationToken cancellationToken);
}

public class CatalogStore(IDbContextFactory<AppDbContext> dbContextFactory, StorageGuard storageGuard) : ICatalogStore
{
    public Task<SavedCatalog?> LoadAsync(string catalog, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"reading the {catalog} catalog", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entries = await db.CatalogEntries
                .AsNoTracking()
                .Where(entry => entry.Catalog == catalog)
                .ToListAsync(cancellationToken);

            return entries.Count == 0
                ? null
                : new SavedCatalog(
                    entries.ToDictionary(entry => entry.Id, entry => entry.Name),
                    entries.Min(entry => entry.UpdatedAt));
        });

    public Task ReplaceAsync(
        string catalog, IReadOnlyDictionary<int, string> names, DateTimeOffset updatedAt, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"saving the {catalog} catalog", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var at = updatedAt.ToUniversalTime();

            // With retries on (EnableRetryOnFailure), a transaction must run inside the execution strategy,
            // so that a retry repeats the whole transaction and not just the statement that failed.
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                // A retry starts from a clean slate: rows added by a failed attempt would be tracked twice.
                db.ChangeTracker.Clear();

                // All or nothing: readers never see a half-replaced catalog.
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.CatalogEntries.Where(entry => entry.Catalog == catalog).ExecuteDeleteAsync(cancellationToken);
                db.CatalogEntries.AddRange(names.Select(pair => new CatalogEntryRecord
                {
                    Catalog = catalog,
                    Id = pair.Key,
                    Name = pair.Value,
                    UpdatedAt = at,
                }));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        });
}
