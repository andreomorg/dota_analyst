using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Models;

// Loads the current patch's game data a situation reads (hero profiles, items, abilities, kits) from the catalogs. The
// catalogs never throw (an unavailable one is empty): an empty part becomes null, so what depends on it is unknown.
public sealed class GameCatalogLoader(IHeroCatalog heroCatalog, IItemCatalog itemCatalog, IAbilityCatalog abilityCatalog)
{
    public async Task<GameCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        var heroesTask = heroCatalog.GetHeroesAsync(cancellationToken);
        var itemsTask = itemCatalog.GetItemsAsync(cancellationToken);
        var abilitiesTask = abilityCatalog.GetAbilitiesAsync(cancellationToken);
        var kitsTask = abilityCatalog.GetKitsAsync(cancellationToken);
        await Task.WhenAll(heroesTask, itemsTask, abilitiesTask, kitsTask);

        var heroes = heroesTask.Result.Values.Where(hero => hero.Details is not null).ToDictionary(hero => hero.Id, hero => hero.Details!);
        var items = itemsTask.Result.Values.Select(item => item.Details).OfType<ItemDetails>().DistinctBy(item => item.Key)
            .ToDictionary(item => item.Key);
        return new GameCatalog(
            heroes.Count == 0 ? null : heroes,
            items.Count == 0 ? null : items,
            abilitiesTask.Result.Count == 0 ? null : abilitiesTask.Result,
            kitsTask.Result.Count == 0 ? null : kitsTask.Result);
    }
}
