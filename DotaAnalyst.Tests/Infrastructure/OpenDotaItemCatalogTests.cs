using System.Net;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Infrastructure.Catalogs;
using DotaAnalyst.Infrastructure.OpenDota;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Infrastructure;

// The item details on top of the layered catalog (LayeredCatalogTests has the layers themselves).
public class OpenDotaItemCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Details_AreSavedAndReadBack()
    {
        var store = new MemoryStore();
        await CreateCatalog(store, new OpenDota()).GetItemsAsync(CancellationToken.None);

        // A new server (empty memory) reads the saved copy, details included, without asking OpenDota.
        var openDota = new OpenDota();
        var items = await CreateCatalog(store, openDota).GetItemsAsync(CancellationToken.None);

        Assert.Equal(0, openDota.Calls);
        Assert.Equal(new ItemDetails("magic_wand", 460, ["magic_stick", "branches", "branches"], false), items[36].Details);
    }

    [Fact]
    public async Task FreshSavedCopyWithoutDetails_IsRefreshedFromOpenDota()
    {
        var store = new MemoryStore { Saved = new SavedCatalog(new Dictionary<int, string> { [36] = "Magic Wand" }, Now) };
        var openDota = new OpenDota();

        var items = await CreateCatalog(store, openDota).GetItemsAsync(CancellationToken.None);

        Assert.Equal(2, openDota.Calls); // item_ids + items
        Assert.NotNull(items[36].Details);
    }

    [Fact]
    public async Task UnreadableSavedDetails_KeepTheName()
    {
        var store = new MemoryStore
        {
            Saved = new SavedCatalog(new Dictionary<int, string> { [36] = "Magic Wand" }, Now)
            {
                Details = new Dictionary<int, string> { [36] = """{"key": "", "cost": -1}""" },
            },
        };

        var items = await CreateCatalog(store, new OpenDota()).GetItemsAsync(CancellationToken.None);

        Assert.Equal("Magic Wand", items[36].Name);
        Assert.Null(items[36].Details);
    }

    private static OpenDotaItemCatalog CreateCatalog(MemoryStore store, OpenDota openDota) =>
        new(
            new OpenDotaClient(new HttpClient(openDota) { BaseAddress = new Uri("https://api.opendota.com/api/") }),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            store,
            new CatalogLoadLocks(),
            new FixedTimeProvider(Now),
            NullLogger<OpenDotaItemCatalog>.Instance);

    private sealed class MemoryStore : ICatalogStore
    {
        public SavedCatalog? Saved { get; set; }

        public Task<SavedCatalog?> LoadAsync(string catalog, CancellationToken cancellationToken) => Task.FromResult(Saved);

        public Task ReplaceAsync(
            string catalog,
            IReadOnlyDictionary<int, string> names,
            IReadOnlyDictionary<int, string> details,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            Saved = new SavedCatalog(names, updatedAt) { Details = details };
            return Task.CompletedTask;
        }
    }

    // GET constants/item_ids and GET constants/items.
    private sealed class OpenDota : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var body = request.RequestUri!.AbsolutePath.EndsWith("item_ids", StringComparison.Ordinal)
                ? """{ "36": "magic_wand" }"""
                : """{ "magic_wand": { "dname": "Magic Wand", "cost": 460, "components": ["magic_stick", "branches", "branches"] } }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
