using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Infrastructure.Catalogs;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Item names and details (key, cost, components) from OpenDota's item constants, kept in memory and in the
// database (see LayeredCatalog).
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

    protected override Item Create(int id, string name, string? details) =>
        new(id, name) { Details = ReadDetails(details) };

    protected override string NameOf(Item item) => item.Name;

    protected override string? DetailsOf(Item item) => item.Details is { } details
        ? JsonSerializer.Serialize(
            new StoredItemDetails(details.Key, details.Cost, details.Components, details.IsConsumable), JsonOptions)
        : null;

    // Copies saved before the item build (sprint 15) have names only.
    protected override bool IsComplete(SavedCatalog saved) => saved.Details.Count > 0;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // A row that can't be read only loses its details (the build), never the item's name.
    private static ItemDetails? ReadDetails(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StoredItemDetails>(json, JsonOptions) is { Key: not null } stored
                ? new ItemDetails(stored.Key, stored.Cost, stored.Components, stored.IsConsumable)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    // The format of catalog_entries.details for items.
    private sealed record StoredItemDetails(string? Key, int Cost, IReadOnlyList<string>? Components, bool IsConsumable);
}
