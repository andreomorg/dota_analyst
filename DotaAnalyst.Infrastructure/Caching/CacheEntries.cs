using Microsoft.Extensions.Caching.Memory;

namespace DotaAnalyst.Infrastructure.Caching;

// Every entry of the shared IMemoryCache goes through here, because with a SizeLimit
// each entry must declare its size (otherwise IMemoryCache throws).
internal static class CacheEntries
{
    // Maximum number of entries (each entry counts as 1).
    public const long SizeLimit = 1000;

    // Catalogs (heroes, items) are needed by every request: evicted last when the cache is full.
    public static MemoryCacheEntryOptions Catalog(TimeSpan duration) => Create(duration, CacheItemPriority.High);

    // One entry per match: there can be many, so they are evicted before catalogs.
    public static MemoryCacheEntryOptions Match(TimeSpan duration) => Create(duration, CacheItemPriority.Normal);

    // Steam profiles (name, avatar) of logged-in users.
    public static MemoryCacheEntryOptions Profile(TimeSpan duration) => Create(duration, CacheItemPriority.Normal);

    // "Not found" answers are cheap to lose and can be flooded with random IDs: evicted first.
    public static MemoryCacheEntryOptions NotFound(TimeSpan duration) => Create(duration, CacheItemPriority.Low);

    private static MemoryCacheEntryOptions Create(TimeSpan duration, CacheItemPriority priority) => new()
    {
        AbsoluteExpirationRelativeToNow = duration,
        Priority = priority,
        Size = 1,
    };
}
