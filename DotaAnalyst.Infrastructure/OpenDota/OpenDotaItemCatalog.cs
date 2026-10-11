using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Infrastructure.Catalogs;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Item names from OpenDota's item constants, kept in memory and in the database (see LayeredCatalog).
public class OpenDotaItemCatalog(
    OpenDotaClient openDotaClient,
    IMemoryCache cache,
    ICatalogStore store,
    CatalogLoadLocks locks,
    TimeProvider timeProvider,
    ILogger<OpenDotaItemCatalog> logger)
    : LayeredCatalog<Item>(cache, store, locks, timeProvider, logger), IItemCatalog
{
    protected override string CatalogName => "items";

    public Task<IReadOnlyDictionary<int, Item>> GetItemsAsync(CancellationToken cancellationToken) =>
        GetAsync(cancellationToken);

    protected override async Task<IReadOnlyDictionary<int, Item>> LoadFromOpenDotaAsync(CancellationToken cancellationToken)
    {
        // The two constants are independent, so both requests run at the same time.
        var itemIdsTask = openDotaClient.GetItemIdsAsync(cancellationToken);
        var itemsTask = openDotaClient.GetItemsAsync(cancellationToken);
        await Task.WhenAll(itemIdsTask, itemsTask);

        return OpenDotaMappings.ToItems(await itemIdsTask, await itemsTask);
    }

    protected override Item Create(int id, string name) => new(id, name);

    protected override string NameOf(Item item) => item.Name;
}
