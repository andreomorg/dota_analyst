using System.Text.Json;
using System.Text.Json.Serialization;
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
            .ToDictionary(hero => hero.Id, hero => new Hero(hero.Id, hero.LocalizedName!) { Details = ToDetails(hero) });
    }

    protected override Hero Create(int id, string name, string? details) =>
        new(id, name) { Details = ReadDetails(details) };

    protected override string NameOf(Hero hero) => hero.Name;

    protected override string? DetailsOf(Hero hero) => hero.Details is { } details
        ? JsonSerializer.Serialize(new StoredHeroDetails(details.PrimaryAttribute, details.AttackType,
            details.Roles.OrderBy(role => role).ToArray(), details.Profile is null ? null : JsonSerializer.SerializeToElement(details.Profile, JsonOptions),
            details.Description is null ? null : JsonSerializer.SerializeToElement(details.Description, JsonOptions)), JsonOptions)
        : null;

    // A partial or unreadable profile must be refreshed too, even if the names were saved recently.
    protected override bool IsComplete(SavedCatalog saved) =>
        saved.Names.Keys.All(id => ReadDetails(saved.Details.GetValueOrDefault(id)) is not null);

    // Enums as text, so reordering one never changes what a saved row means; rows saved with numbers still read.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static HeroDetails? ToDetails(OpenDotaHero hero)
    {
        HeroAttribute? attribute = hero.PrimaryAttr switch
        {
            "str" => HeroAttribute.Strength,
            "agi" => HeroAttribute.Agility,
            "int" => HeroAttribute.Intelligence,
            "all" => HeroAttribute.Universal,
            _ => null,
        };
        HeroAttackType? attack = hero.AttackType switch
        {
            "Melee" => HeroAttackType.Melee,
            "Ranged" => HeroAttackType.Ranged,
            _ => null,
        };
        if (attribute is null || attack is null || hero.Roles is null)
        {
            return null;
        }

        // New upstream roles must not discard the known roles or prevent loading the catalog.
        var roles = hero.Roles.Select(name => name switch
        {
            "Carry" => (HeroRole?)HeroRole.Carry,
            "Support" => HeroRole.Support,
            "Nuker" => HeroRole.Nuker,
            "Disabler" => HeroRole.Disabler,
            "Initiator" => HeroRole.Initiator,
            "Escape" => HeroRole.Escape,
            "Durable" => HeroRole.Durable,
            "Pusher" => HeroRole.Pusher,
            "Jungler" => HeroRole.Jungler,
            _ => null,
        }).OfType<HeroRole>().ToHashSet();
        return new HeroDetails(attribute.Value, attack.Value, roles);
    }

    private static HeroDetails? ReadDetails(string? json)
    {
        if (json is null)
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<StoredHeroDetails>(json, JsonOptions) is
                { PrimaryAttribute: { } attribute, AttackType: { } attack, Roles: { } roles }
                ? new HeroDetails(attribute, attack, roles.ToHashSet()) { Profile = ReadProfile(json) }
                : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // A damaged profile must not hide a usable hero name.
            return null;
        }
    }

    private sealed record StoredHeroDetails(HeroAttribute? PrimaryAttribute, HeroAttackType? AttackType,
        IReadOnlyList<HeroRole>? Roles, JsonElement? Profile = null, JsonElement? Description = null);

    private static HeroProfile? ReadProfile(string json)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<StoredHeroDetails>(json, JsonOptions)?.Profile;
            var profile = raw is { ValueKind: JsonValueKind.Object } p ? p.Deserialize<HeroProfile>(JsonOptions) : null;
            return profile?.HasValidShape() == true ? profile : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { return null; }
    }
}
