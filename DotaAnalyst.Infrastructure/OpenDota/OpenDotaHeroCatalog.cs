using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Infrastructure.Catalogs;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Hero names from OpenDota's GET /heroes, kept in memory and in the database (see LayeredCatalog).
public class OpenDotaHeroCatalog(
    OpenDotaClient openDotaClient,
    IMemoryCache cache,
    ICatalogStore store,
    CatalogLoadLocks locks,
    TimeProvider timeProvider,
    ILogger<OpenDotaHeroCatalog> logger)
    : LayeredCatalog<Hero>(cache, store, locks, timeProvider, logger), IHeroCatalog
{
    protected override string CatalogName => "heroes";

    public Task<IReadOnlyDictionary<int, Hero>> GetHeroesAsync(CancellationToken cancellationToken) =>
        GetAsync(cancellationToken);

    protected override async Task<IReadOnlyDictionary<int, Hero>> LoadFromOpenDotaAsync(CancellationToken cancellationToken)
    {
        var response = await openDotaClient.GetHeroesAsync(cancellationToken);
        return response
            .Where(hero => !string.IsNullOrWhiteSpace(hero.LocalizedName))
            .DistinctBy(hero => hero.Id)
            .ToDictionary(hero => hero.Id, hero => new Hero(hero.Id, hero.LocalizedName!));
    }

    protected override Hero Create(int id, string name) => new(id, name);

    protected override string NameOf(Hero hero) => hero.Name;
}
