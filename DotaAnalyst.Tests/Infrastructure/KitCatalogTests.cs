using System.Net;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure.Catalogs;
using DotaAnalyst.Infrastructure.OpenDota;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Infrastructure;

public sealed class KitCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static OpenDotaAbilityCatalog Abilities(KitStore store, KitHttp http) => new(Client(http), Cache(), store,
        new CatalogLoadLocks(), new FixedTimeProvider(Now), NullLogger<OpenDotaAbilityCatalog>.Instance);
    private static OpenDotaItemCatalog Items(KitStore store, KitHttp http) => new(Client(http), Cache(), store,
        new CatalogLoadLocks(), new FixedTimeProvider(Now), NullLogger<OpenDotaItemCatalog>.Instance);
    private static MemoryCache Cache() => new(new MemoryCacheOptions { SizeLimit = 1000 });
    private static OpenDotaClient Client(KitHttp http) => new(new HttpClient(http) { BaseAddress = new("https://offline.test/") });

    [Fact]
    public async Task Abilities_RoundTripPatchKitsAndDetails_ThenUseMemoryAndDatabase()
    {
        var store = new KitStore();
        var http = new KitHttp();
        var catalog = Abilities(store, http);
        var abilities = await catalog.GetAbilitiesAsync(default);
        Assert.Equal(5, http.Calls);
        var kit = (await catalog.GetKitsAsync(default))[26];
        Assert.Equal(26, kit.HeroId);
        Assert.Equal("7.41", await catalog.GetPatchAsync(default));
        Assert.Equal(5, http.Calls);
        var offline = new KitHttp { Fails = true };
        var restarted = Abilities(store, offline);
        var reloaded = await restarted.GetAbilitiesAsync(default);
        Assert.Equal(abilities["lion_impale"], reloaded["lion_impale"]);
        Assert.Equal(abilities["lion_mana_drain"], reloaded["lion_mana_drain"]);
        Assert.True(reloaded["lion_to_hell_and_back"].IsInnate);
        Assert.Equal([25d, 50, 75, 150], reloaded["centaur_khan_war_stomp"].BaseDamage);
        Assert.Equal("7.41", await restarted.GetPatchAsync(default));
        var restartedKit = (await restarted.GetKitsAsync(default))[26];
        Assert.Equal(kit, restartedKit);
        Assert.Equal(5, restartedKit.Abilities.Count);
        Assert.Equal("Mana Drain", restartedKit.Shard!.AbilityName);
        Assert.Equal("+{s:bonus_movespeed}% Mana Drain Slow", restartedKit.Talents[0].Name);
        Assert.Equal(0, offline.Calls);
    }

    [Fact]
    public async Task AbilitySnapshotWithoutDetails_IsRefreshed()
    {
        var store = new KitStore { Saved = new(new Dictionary<int, string> { [1] = "current" }, Now) };
        var http = new KitHttp();
        Assert.NotEmpty(await Abilities(store, http).GetAbilitiesAsync(default));
        Assert.Equal(5, http.Calls);
    }

    [Fact]
    public async Task VersionOneSnapshot_IsRefreshedWhenFresh_AndStillUsedWhenOpenDotaFails()
    {
        const string v1 = """{"version":1,"patch":"7.40","abilities":{"lion_impale":{"key":"lion_impale","name":"Earth Spike","behavior":[],"targetTypes":[],"cooldown":[],"manaCost":[90],"values":[]}},"kits":{"26":{"heroId":26,"abilities":["lion_impale"],"talents":[],"facets":[{"id":0,"key":"f","deprecated":true}]}}}""";
        SavedCatalog Saved() => new(new Dictionary<int, string> { [1] = "current" }, Now) { Details = new Dictionary<int, string> { [1] = v1 } };
        var http = new KitHttp();
        Assert.Equal("7.41", await Abilities(new KitStore { Saved = Saved() }, http).GetPatchAsync(default));
        Assert.Equal(5, http.Calls);
        var offline = Abilities(new KitStore { Saved = Saved() }, new() { Fails = true });
        Assert.Equal("7.40", await offline.GetPatchAsync(default));
        Assert.Empty((await offline.GetKitsAsync(default))[26].Facets[0].Abilities);
        Assert.Empty((await offline.GetAbilitiesAsync(default))["lion_impale"].BaseDamage);
    }

    [Fact]
    public async Task AghanimUnavailable_KitStillLoadsWithoutIt()
    {
        var http = new KitHttp { AghanimFails = true };
        var kit = (await Abilities(new KitStore(), http).GetKitsAsync(default))[26];
        Assert.Null(kit.Scepter);
        Assert.Equal(5, kit.Abilities.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Abilities_UnavailableOrEmpty_UseOldSnapshot(bool empty)
    {
        var store = new KitStore();
        await Abilities(store, new()).GetAbilitiesAsync(default);
        var old = store.Saved!;
        store.Saved = old with { UpdatedAt = Now.AddDays(-2) };
        var http = new KitHttp { Fails = !empty, Empty = empty };
        Assert.Equal("7.41", await Abilities(store, http).GetPatchAsync(default));
        Assert.Equal(old.Details, store.Saved.Details);
    }

    [Fact]
    public async Task NoDatabaseAndNoSource_ReturnEmpty_AndRetryIsCached()
    {
        var http = new KitHttp { Fails = true };
        var catalog = Abilities(new KitStore { Fails = true }, http);
        Assert.Empty(await catalog.GetAbilitiesAsync(default));
        var calls = http.Calls;
        Assert.Empty(await catalog.GetKitsAsync(default));
        Assert.Null(await catalog.GetPatchAsync(default));
        Assert.Equal(calls, http.Calls);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{}")]
    public async Task CorruptSavedSnapshot_IsRefreshed(string json)
    {
        var store = new KitStore
        {
            Saved = new(new Dictionary<int, string> { [1] = "current" }, Now)
            { Details = new Dictionary<int, string> { [1] = json } }
        };
        Assert.NotEmpty(await Abilities(store, new()).GetAbilitiesAsync(default));
    }

    [Fact]
    public async Task Items_RoundTripNewFields()
    {
        var store = new KitStore();
        await Items(store, new()).GetItemsAsync(default);
        var http = new KitHttp { Fails = true };
        var items = await Items(store, http).GetItemsAsync(default);
        Assert.Equal(0, http.Calls);
        var bkb = items[116].Details!;
        Assert.Equal("Avatar", bkb.Abilities[0].Title);
        Assert.Equal([9d, 8, 7], bkb.Values.Single(v => v.Key == "duration").Levels);
        Assert.Equal(95, bkb.Cooldown);
        Assert.Equal(50, bkb.ManaCost);
        Assert.Equal(["Instant Cast", "No Target"], bkb.Behavior);
        Assert.False(bkb.HasCharges);
    }

    [Fact]
    public async Task LegacyItemDetails_AreRefreshed_EvenWhenFresh()
    {
        var store = new KitStore
        {
            Saved = new(new Dictionary<int, string> { [116] = "Black King Bar" }, Now)
            { Details = new Dictionary<int, string> { [116] = """{"key":"black_king_bar","cost":4050,"components":[],"isConsumable":false}""" } }
        };
        var http = new KitHttp();
        Assert.NotEmpty((await Items(store, http).GetItemsAsync(default))[116].Details!.Abilities);
        Assert.Equal(2, http.Calls);
    }

    [Fact]
    public async Task LegacyItemDetails_RemainUsableIfSourceFails()
    {
        var store = new KitStore
        {
            Saved = new(new Dictionary<int, string> { [116] = "Black King Bar" }, Now)
            { Details = new Dictionary<int, string> { [116] = """{"key":"black_king_bar","cost":4050,"components":[],"isConsumable":false}""" } }
        };
        var item = (await Items(store, new() { Fails = true }).GetItemsAsync(default))[116];
        Assert.Equal(4050, item.Details!.Cost);
        Assert.Empty(item.Details.Abilities);
    }

    private sealed class KitStore : ICatalogStore
    {
        public SavedCatalog? Saved { get; set; }
        public bool Fails { get; set; }
        public Task<SavedCatalog?> LoadAsync(string catalog, CancellationToken cancellationToken) =>
            Fails ? throw new StorageUnavailableException("offline") : Task.FromResult(Saved);
        public Task ReplaceAsync(string catalog, IReadOnlyDictionary<int, string> names,
            IReadOnlyDictionary<int, string> details, DateTimeOffset updatedAt, CancellationToken cancellationToken)
        {
            if (Fails) throw new StorageUnavailableException("offline");
            Saved = new(names, updatedAt) { Details = details };
            return Task.CompletedTask;
        }
    }

    private sealed class KitHttp : HttpMessageHandler
    {
        public int Calls;
        public bool Fails, Empty, AghanimFails;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (Fails || (AghanimFails && request.RequestUri!.AbsolutePath == "/constants/aghs_desc"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/heroes" => """[{"id":26,"localized_name":"Lion","name":"npc_dota_hero_lion"}]""",
                "/constants/abilities" => Empty ? "{}" : KitSamples.Abilities,
                "/constants/hero_abilities" => KitSamples.Kits,
                "/constants/patch" => """[{"name":"7.40"},{"name":"7.41"}]""",
                "/constants/aghs_desc" => KitSamples.Aghanim,
                "/constants/item_ids" => """{"1":"blink","116":"black_king_bar"}""",
                "/constants/items" => KitSamples.Items,
                _ => throw new InvalidOperationException("Unexpected offline request"),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
