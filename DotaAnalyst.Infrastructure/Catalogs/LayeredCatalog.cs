using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure.Caching;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.Catalogs;

// A catalog of names (heroes, items) looked up in layers: memory -> our database -> OpenDota.
// - A saved copy younger than FreshFor is used without asking OpenDota (e.g. right after the server wakes up).
// - When OpenDota fails, an older saved copy is still better than "Unknown hero" everywhere.
// - Only one request at a time loads a catalog; the others wait and then read it from memory.
// Neither a database nor an OpenDota failure ever breaks the response: the worst case is an empty catalog.
public abstract class LayeredCatalog<T>(
    IMemoryCache cache,
    ICatalogStore store,
    CatalogLoadLocks locks,
    TimeProvider timeProvider,
    ILogger logger)
    where T : class
{
    // Names only change with patches.
    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(24);
    // After an OpenDota failure, wait a bit before asking again, so every request isn't slowed down by it.
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    // Name of the catalog in the cache keys, the database and the logs ("heroes", "items").
    protected abstract string CatalogName { get; }

    /// <exception cref="DataSourceUnavailableException">OpenDota could not be reached.</exception>
    protected abstract Task<IReadOnlyDictionary<int, T>> LoadFromOpenDotaAsync(CancellationToken cancellationToken);

    // An entry from the saved copy; details is its extra data as saved by DetailsOf (null when it has none).
    protected abstract T Create(int id, string name, string? details);

    protected abstract string NameOf(T entry);

    // The entry's extra data, saved as JSON next to its name; null when the catalog has none (heroes).
    protected virtual string? DetailsOf(T entry) => null;

    // False for a saved copy made before the catalog had something it needs now: then it is treated as old
    // (refreshed from OpenDota, still used if OpenDota fails).
    protected virtual bool IsComplete(SavedCatalog saved) => true;

    private string CacheKey => $"catalog:{CatalogName}";

    protected async Task<IReadOnlyDictionary<int, T>> GetAsync(CancellationToken cancellationToken)
    {
        if (TryGetCached(out var cached))
        {
            return cached;
        }

        var catalogLock = locks.For(CatalogName);
        await catalogLock.WaitAsync(cancellationToken);
        try
        {
            // Checked again: the request that held the lock may have just loaded it.
            if (TryGetCached(out cached))
            {
                return cached;
            }

            var (entries, cacheFor) = await LoadAsync(cancellationToken);
            cache.Set(CacheKey, entries, CacheEntries.Catalog(cacheFor));
            return entries;
        }
        finally
        {
            catalogLock.Release();
        }
    }

    private bool TryGetCached(out IReadOnlyDictionary<int, T> entries)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<int, T>? cached) && cached is not null)
        {
            entries = cached;
            return true;
        }

        entries = default!;
        return false;
    }

    // Returns the catalog and how long to keep it in memory.
    private async Task<(IReadOnlyDictionary<int, T> Entries, TimeSpan CacheFor)> LoadAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var saved = await TryLoadSavedAsync(cancellationToken);
        if (saved is not null)
        {
            // Never negative, even if the clocks of two servers disagree a little.
            var savedAge = now - saved.UpdatedAt > TimeSpan.Zero ? now - saved.UpdatedAt : TimeSpan.Zero;
            if (savedAge < FreshFor && IsComplete(saved))
            {
                // Kept in memory only until the saved copy itself gets old.
                return (ToEntries(saved), FreshFor - savedAge);
            }
        }

        try
        {
            var loaded = await LoadFromOpenDotaAsync(cancellationToken);
            if (loaded.Count == 0)
            {
                // A "successful" empty answer (e.g. OpenDota changed its format) is treated like a failure:
                // never cached for a whole day, never saved over a good copy.
                throw new DataSourceUnavailableException($"OpenDota returned an empty {CatalogName} list.");
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Loaded {Count} {Catalog} from OpenDota", loaded.Count, CatalogName);
            }
            await TrySaveAsync(loaded, now, cancellationToken);
            return (loaded, FreshFor);
        }
        catch (DataSourceUnavailableException ex) when (saved is not null)
        {
            logger.LogWarning(
                ex, "Could not load {Catalog} from OpenDota; using the copy saved at {SavedAt}", CatalogName, saved.UpdatedAt);
            return (ToEntries(saved), RetryAfterFailure);
        }
        catch (DataSourceUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not load {Catalog} from OpenDota and there is no saved copy", CatalogName);
            return (new Dictionary<int, T>(), RetryAfterFailure);
        }
    }

    private async Task<SavedCatalog?> TryLoadSavedAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await store.LoadAsync(CatalogName, cancellationToken);
        }
        catch (StorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not read the saved {Catalog} from the database", CatalogName);
            return null;
        }
    }

    private async Task TrySaveAsync(IReadOnlyDictionary<int, T> entries, DateTimeOffset loadedAt, CancellationToken cancellationToken)
    {
        try
        {
            var names = entries.ToDictionary(pair => pair.Key, pair => NameOf(pair.Value));
            var details = entries
                .Select(pair => (pair.Key, Details: DetailsOf(pair.Value)))
                .Where(entry => entry.Details is not null)
                .ToDictionary(entry => entry.Key, entry => entry.Details!);
            await store.ReplaceAsync(CatalogName, names, details, loadedAt, cancellationToken);
        }
        catch (StorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not save the {Catalog} to the database", CatalogName);
        }
    }

    private Dictionary<int, T> ToEntries(SavedCatalog saved) =>
        saved.Names.ToDictionary(pair => pair.Key, pair => Create(pair.Key, pair.Value, saved.Details.GetValueOrDefault(pair.Key)));
}
