using System.Net;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Infrastructure.Catalogs;
using DotaAnalyst.Infrastructure.OpenDota;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Infrastructure;

// The layers of a catalog (memory -> database -> OpenDota), tested through the hero catalog with a fake
// database and a fake OpenDota. The real database part (CatalogStore) has its own tests against PostgreSQL.
public class LayeredCatalogTests
{
    private const string HeroesJson = """[{"id":31,"localized_name":"Lich"},{"id":75,"localized_name":"Silencer"}]""";
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FreshSavedCopy_IsUsedWithoutAskingOpenDota()
    {
        var store = new FakeCatalogStore { Saved = Saved(age: TimeSpan.FromHours(23)) };
        var openDota = new StubOpenDota(HttpStatusCode.OK, HeroesJson);

        var heroes = await CreateCatalog(store, openDota).GetHeroesAsync(CancellationToken.None);

        Assert.Equal("Saved Lich", heroes[31].Name);
        Assert.Equal(0, openDota.Calls);
    }

    [Fact]
    public async Task OldSavedCopy_IsRefreshedFromOpenDotaAndSaved()
    {
        var store = new FakeCatalogStore { Saved = Saved(age: TimeSpan.FromHours(25)) };
        var openDota = new StubOpenDota(HttpStatusCode.OK, HeroesJson);

        var heroes = await CreateCatalog(store, openDota).GetHeroesAsync(CancellationToken.None);

        Assert.Equal("Lich", heroes[31].Name);
        Assert.Equal("Silencer", store.Saved!.Names[75]);
        Assert.Equal(Now, store.Saved.UpdatedAt);
    }

    [Fact]
    public async Task OpenDotaDown_UsesOldSavedCopy()
    {
        var store = new FakeCatalogStore { Saved = Saved(age: TimeSpan.FromDays(30)) };

        var heroes = await CreateCatalog(store, new StubOpenDota(HttpStatusCode.InternalServerError, "oops"))
            .GetHeroesAsync(CancellationToken.None);

        Assert.Equal("Saved Lich", heroes[31].Name);
    }

    [Fact]
    public async Task OpenDotaAnswersEmptyList_UsesOldSavedCopyAndKeepsIt()
    {
        var store = new FakeCatalogStore { Saved = Saved(age: TimeSpan.FromDays(2)) };

        var heroes = await CreateCatalog(store, new StubOpenDota(HttpStatusCode.OK, "[]"))
            .GetHeroesAsync(CancellationToken.None);

        Assert.Equal("Saved Lich", heroes[31].Name);
        Assert.Equal("Saved Lich", store.Saved!.Names[31]);
    }

    [Fact]
    public async Task OpenDotaDownAndNothingSaved_ReturnsEmptyCatalog()
    {
        var heroes = await CreateCatalog(new FakeCatalogStore(), new StubOpenDota(HttpStatusCode.InternalServerError, "oops"))
            .GetHeroesAsync(CancellationToken.None);

        Assert.Empty(heroes);
    }

    [Fact]
    public async Task DatabaseDown_StillLoadsFromOpenDota()
    {
        var heroes = await CreateCatalog(new FakeCatalogStore { Fail = true }, new StubOpenDota(HttpStatusCode.OK, HeroesJson))
            .GetHeroesAsync(CancellationToken.None);

        Assert.Equal("Lich", heroes[31].Name);
    }

    [Fact]
    public async Task SimultaneousFirstRequests_LoadFromOpenDotaOnce()
    {
        // Shared by every "request", like the singletons in the app; each request gets its own catalog (Scoped).
        var cache = NewCache();
        var locks = new CatalogLoadLocks();
        var store = new FakeCatalogStore();
        var openDota = new StubOpenDota(HttpStatusCode.OK, HeroesJson, delay: TimeSpan.FromMilliseconds(200));

        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => CreateCatalog(store, openDota, cache, locks).GetHeroesAsync(CancellationToken.None)));

        Assert.Equal(1, openDota.Calls);
        Assert.All(results, heroes => Assert.Equal("Lich", heroes[31].Name));
    }

    [Fact]
    public async Task SecondRequest_ComesFromMemory()
    {
        var cache = NewCache();
        var store = new FakeCatalogStore();
        var openDota = new StubOpenDota(HttpStatusCode.OK, HeroesJson);

        await CreateCatalog(store, openDota, cache).GetHeroesAsync(CancellationToken.None);
        await CreateCatalog(store, openDota, cache).GetHeroesAsync(CancellationToken.None);

        Assert.Equal(1, openDota.Calls);
        Assert.Equal(1, store.Loads);
    }

    private static SavedCatalog Saved(TimeSpan age) =>
        new(new Dictionary<int, string> { [31] = "Saved Lich" }, Now - age);

    private static MemoryCache NewCache() => new(new MemoryCacheOptions { SizeLimit = 1000 });

    private static OpenDotaHeroCatalog CreateCatalog(
        FakeCatalogStore store, StubOpenDota openDota, IMemoryCache? cache = null, CatalogLoadLocks? locks = null)
    {
        var httpClient = new HttpClient(openDota) { BaseAddress = new Uri("https://api.opendota.com/api/") };
        return new OpenDotaHeroCatalog(
            new OpenDotaClient(httpClient),
            cache ?? NewCache(),
            store,
            locks ?? new CatalogLoadLocks(),
            new FixedTimeProvider(Now),
            NullLogger<OpenDotaHeroCatalog>.Instance);
    }

    private sealed class FakeCatalogStore : ICatalogStore
    {
        public SavedCatalog? Saved { get; set; }

        public bool Fail { get; init; }

        public int Loads { get; private set; }

        public Task<SavedCatalog?> LoadAsync(string catalog, CancellationToken cancellationToken)
        {
            Loads++;
            return Fail ? throw new StorageUnavailableException("The database is down.") : Task.FromResult(Saved);
        }

        public Task ReplaceAsync(
            string catalog,
            IReadOnlyDictionary<int, string> names,
            IReadOnlyDictionary<int, string> details,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new StorageUnavailableException("The database is down.");
            }

            Saved = new SavedCatalog(names, updatedAt) { Details = details };
            return Task.CompletedTask;
        }
    }

    private sealed class StubOpenDota(HttpStatusCode statusCode, string body, TimeSpan delay = default) : HttpMessageHandler
    {
        private int calls;

        public int Calls => calls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(statusCode) { Content = new StringContent(body) };
        }
    }
}
