using System.Collections.Concurrent;

namespace DotaAnalyst.Infrastructure.Catalogs;

// One lock per catalog, shared by the whole app (registered as a singleton). The catalogs themselves are
// Scoped (a new instance per request), so a lock kept inside them would not stop two requests at once.
public sealed class CatalogLoadLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();

    // SemaphoreSlim(1, 1) is a lock that works with await (the C# "lock" statement does not allow await inside).
    public SemaphoreSlim For(string catalog) => locks.GetOrAdd(catalog, _ => new SemaphoreSlim(1, 1));
}
