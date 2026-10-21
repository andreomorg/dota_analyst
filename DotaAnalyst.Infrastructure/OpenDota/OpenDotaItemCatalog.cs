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
            new StoredItemDetails(details.Key, details.Cost, details.Components, details.IsConsumable, 2,
                details.Abilities, details.Behavior, details.Cooldown, details.ManaCost, details.Values, details.HasCharges), JsonOptions)
        : null;

    // Names-only copies and readable details saved before the current ability fields must be refreshed. An item without
    // details or with unreadable ones does not force a reload (it would reload on every cache miss): its details just go.
    protected override bool IsComplete(SavedCatalog saved) =>
        saved.Details.Count > 0 && saved.Details.Values.All(json => ReadDetails(json) is null || IsCurrent(json));

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
                {
                    Abilities = stored.Abilities ?? [], Behavior = stored.Behavior ?? [], Cooldown = stored.Cooldown,
                    ManaCost = stored.ManaCost, Values = stored.Values ?? [], HasCharges = stored.HasCharges,
                }
                : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    // The format of catalog_entries.details for items.
    private static bool IsCurrent(string? json)
    {
        try
        {
            return json is not null && ReadDetails(json) is not null
                && JsonSerializer.Deserialize<StoredItemDetails>(json, JsonOptions) is
                    { Version: 2, Abilities: not null, Behavior: not null, Values: not null };
        }
        catch (JsonException) { return false; }
    }

    private sealed record StoredItemDetails(string? Key, int Cost, IReadOnlyList<string>? Components, bool IsConsumable,
        int Version = 0, IReadOnlyList<ItemAbility>? Abilities = null, IReadOnlyList<string>? Behavior = null,
        double? Cooldown = null, double? ManaCost = null,
        IReadOnlyList<DotaAnalyst.Domain.Abilities.AbilityValue>? Values = null, bool HasCharges = false);
}
