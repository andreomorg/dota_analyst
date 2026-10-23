using System.Net;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Infrastructure.Catalogs;
using DotaAnalyst.Infrastructure.OpenDota;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Infrastructure;

// Profile mapping and storage use an HTTP stub and an in-memory store, without external services.
public class OpenDotaHeroCatalogTests
{
    [Fact]
    public async Task DamagedExtendedProfileDoesNotDiscardLegacyClassificationOrRefreshForever()
    {
        var store = new MemoryStore
        {
            Saved = new SavedCatalog(new Dictionary<int, string> { [31] = "Lich" }, Now)
            { Details = new Dictionary<int, string> { [31] = """{"primaryAttribute":2,"attackType":1,"roles":[1],"profile":{"attributes":{"strength":{"value":10,"origin":99}}}}""" } }
        };
        var source = new OpenDota { Fail = true };
        var heroes = await CreateCatalog(store, source).GetHeroesAsync(default);
        Assert.Equal(HeroAttribute.Intelligence, heroes[31].Details!.PrimaryAttribute);
        Assert.Null(heroes[31].Details!.Profile);
        Assert.Equal(0, source.Calls);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HeroesJson_MapsProfileAndIgnoresUnknownRoles()
    {
        var heroes = await CreateCatalog(new MemoryStore(), new OpenDota()).GetHeroesAsync(CancellationToken.None);

        Assert.Equal("Lich", heroes[31].Name);
        Assert.Equal(HeroAttribute.Intelligence, heroes[31].Details!.PrimaryAttribute);
        Assert.Equal(HeroAttackType.Ranged, heroes[31].Details!.AttackType);
        Assert.True(heroes[31].Details!.Roles.SetEquals([HeroRole.Support, HeroRole.Nuker]));
    }

    [Theory]
    [InlineData("str", "Melee", HeroAttribute.Strength, HeroAttackType.Melee)]
    [InlineData("agi", "Ranged", HeroAttribute.Agility, HeroAttackType.Ranged)]
    [InlineData("int", "Ranged", HeroAttribute.Intelligence, HeroAttackType.Ranged)]
    [InlineData("all", "Melee", HeroAttribute.Universal, HeroAttackType.Melee)]
    public async Task HeroesJson_MapsEachAttribute(string attribute, string attack, HeroAttribute expectedAttribute,
        HeroAttackType expectedAttack)
    {
        var source = new OpenDota { Body = $$"""[{"id":31,"localized_name":"Hero","primary_attr":"{{attribute}}","attack_type":"{{attack}}","roles":[]}]""" };
        var heroes = await CreateCatalog(new MemoryStore(), source).GetHeroesAsync(CancellationToken.None);

        Assert.Equal(expectedAttribute, heroes[31].Details!.PrimaryAttribute);
        Assert.Equal(expectedAttack, heroes[31].Details!.AttackType);
    }

    [Fact]
    public async Task Details_AreSavedAndReadBack()
    {
        var store = new MemoryStore();
        await CreateCatalog(store, new OpenDota()).GetHeroesAsync(CancellationToken.None);

        Assert.Contains("\"primaryAttribute\"", store.Saved!.Details[31]);

        // A new server (empty memory) reads the saved copy, details included, without asking OpenDota.
        var openDota = new OpenDota();
        var heroes = await CreateCatalog(store, openDota).GetHeroesAsync(CancellationToken.None);

        Assert.Equal(0, openDota.Calls);
        Assert.Equal(new HeroDetails(HeroAttribute.Intelligence, HeroAttackType.Ranged, new HashSet<HeroRole> { HeroRole.Support, HeroRole.Nuker }), heroes[31].Details);
    }

    [Fact]
    public async Task FreshSavedCopyWithoutDetails_IsRefreshedFromOpenDota()
    {
        var store = new MemoryStore { Saved = new SavedCatalog(new Dictionary<int, string> { [31] = "Lich" }, Now) };
        var openDota = new OpenDota();

        var heroes = await CreateCatalog(store, openDota).GetHeroesAsync(CancellationToken.None);

        Assert.Equal(1, openDota.Calls);
        Assert.NotNull(heroes[31].Details);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"primaryAttribute\":99,\"attackType\":1,\"roles\":[]}")]
    [InlineData("{\"primaryAttribute\":2,\"attackType\":1,\"roles\":[99]}")]
    public async Task UnreadableSavedDetails_KeepTheName(string json)
    {
        var store = new MemoryStore
        {
            Saved = new SavedCatalog(new Dictionary<int, string> { [31] = "Lich" }, Now)
            {
                Details = new Dictionary<int, string> { [31] = json },
            },
        };

        var heroes = await CreateCatalog(store, new OpenDota { Fail = true }).GetHeroesAsync(CancellationToken.None);

        Assert.Equal("Lich", heroes[31].Name);
        Assert.Null(heroes[31].Details);
    }

    private static OpenDotaHeroCatalog CreateCatalog(MemoryStore store, OpenDota openDota) =>
        new(
            new OpenDotaClient(new HttpClient(openDota) { BaseAddress = new Uri("https://api.opendota.com/api/") }),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            store,
            new CatalogLoadLocks(),
            new FixedTimeProvider(Now),
            NullLogger<OpenDotaHeroCatalog>.Instance);

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

    // GET /heroes, including a duplicate and an unknown role.
    private sealed class OpenDota : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public string? Body { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var body = """[{"id":31,"localized_name":"Lich","primary_attr":"int","attack_type":"Ranged","roles":["Support","Nuker","FutureRole","Support"]}]""";
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(Body ?? body) });
        }
    }
}
