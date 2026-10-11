using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Items;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Item names only change with patches, so they are cached like the hero names (see OpenDotaHeroCatalog).
public class OpenDotaItemCatalog(
    OpenDotaClient openDotaClient,
    IMemoryCache cache,
    ILogger<OpenDotaItemCatalog> logger) : IItemCatalog
{
    private const string CacheKey = "opendota:items";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    // After a failure, wait a bit before asking OpenDota again, so every request isn't slowed down by it.
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyDictionary<int, Item>> GetItemsAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<int, Item>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            // The two constants are independent, so both requests run at the same time.
            var itemIdsTask = openDotaClient.GetItemIdsAsync(cancellationToken);
            var itemsTask = openDotaClient.GetItemsAsync(cancellationToken);
            await Task.WhenAll(itemIdsTask, itemsTask);

            var items = OpenDotaMappings.ToItems(await itemIdsTask, await itemsTask);
            cache.Set(CacheKey, items, CacheDuration);
            logger.LogInformation("Loaded {ItemCount} items from OpenDota", items.Count);
            return items;
        }
        catch (DataSourceUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not load items from OpenDota");
            IReadOnlyDictionary<int, Item> empty = new Dictionary<int, Item>();
            cache.Set(CacheKey, empty, FailureCacheDuration);
            return empty;
        }
    }
}
