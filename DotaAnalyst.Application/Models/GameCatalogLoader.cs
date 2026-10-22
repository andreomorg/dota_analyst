using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Models;

// Loads the game data a situation reads (hero profiles, items, abilities, kits). First the build the match was played
// on (IGameBuildCatalog: the game's own files, every characteristic of each ability); when that build is not known
// here, the current patch from the catalogs. The catalogs never throw (an unavailable one is empty): an empty part
// becomes null, so what depends on it is unknown. The map is the one of the match's patch (IGameMapCatalog), either way.
public sealed class GameCatalogLoader(IHeroCatalog heroCatalog, IItemCatalog itemCatalog, IAbilityCatalog abilityCatalog,
    IGameBuildCatalog buildCatalog, IGameMapCatalog? mapCatalog = null)
{
    public async Task<GameCatalog> LoadAsync(CancellationToken cancellationToken) =>
        (await LoadForMatchAsync(null, cancellationToken)).Catalog;

    // Build: the game build used, null when the current catalogs were.
    public async Task<(GameCatalog Catalog, GameBuild? Build)> LoadForMatchAsync(DateTimeOffset? startedAt, CancellationToken cancellationToken)
    {
        var (catalog, build) = await LoadDataAsync(startedAt, cancellationToken);
        return (mapCatalog?.ForMatch(startedAt) is { } map ? catalog with { Map = map } : catalog, build);
    }

    private async Task<(GameCatalog Catalog, GameBuild? Build)> LoadDataAsync(DateTimeOffset? startedAt, CancellationToken cancellationToken)
    {
        if (startedAt is not null && await buildCatalog.ForMatchAsync(startedAt, cancellationToken) is { } build)
        {
            return (build.Catalog, build.Build);
        }

        var heroesTask = heroCatalog.GetHeroesAsync(cancellationToken);
        var itemsTask = itemCatalog.GetItemsAsync(cancellationToken);
        var abilitiesTask = abilityCatalog.GetAbilitiesAsync(cancellationToken);
        var kitsTask = abilityCatalog.GetKitsAsync(cancellationToken);
        await Task.WhenAll(heroesTask, itemsTask, abilitiesTask, kitsTask);

        var heroes = heroesTask.Result.Values.Where(hero => hero.Details is not null).ToDictionary(hero => hero.Id, hero => hero.Details!);
        var items = itemsTask.Result.Values.Select(item => item.Details).OfType<ItemDetails>().DistinctBy(item => item.Key)
            .ToDictionary(item => item.Key);
        return (new GameCatalog(
            heroes.Count == 0 ? null : heroes,
            items.Count == 0 ? null : items,
            abilitiesTask.Result.Count == 0 ? null : abilitiesTask.Result,
            kitsTask.Result.Count == 0 ? null : kitsTask.Result), null);
    }
}
