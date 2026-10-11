using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Hero names rarely change, so the OpenDota hero list is cached instead of fetched on every request.
public class OpenDotaHeroCatalog(
    OpenDotaClient openDotaClient,
    IMemoryCache cache,
    ILogger<OpenDotaHeroCatalog> logger) : IHeroCatalog
{
    private const string CacheKey = "opendota:heroes";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    // After a failure, wait a bit before asking OpenDota again, so every request isn't slowed down by it.
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyDictionary<int, Hero>> GetHeroesAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<int, Hero>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var response = await openDotaClient.GetHeroesAsync(cancellationToken);
            IReadOnlyDictionary<int, Hero> heroes = response
                .Where(hero => !string.IsNullOrWhiteSpace(hero.LocalizedName))
                .DistinctBy(hero => hero.Id)
                .ToDictionary(hero => hero.Id, hero => new Hero(hero.Id, hero.LocalizedName!));
            cache.Set(CacheKey, heroes, CacheEntries.Catalog(CacheDuration));
            logger.LogInformation("Loaded {HeroCount} heroes from OpenDota", heroes.Count);
            return heroes;
        }
        catch (DataSourceUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not load heroes from OpenDota");
            IReadOnlyDictionary<int, Hero> empty = new Dictionary<int, Hero>();
            cache.Set(CacheKey, empty, CacheEntries.Catalog(FailureCacheDuration));
            return empty;
        }
    }
}
