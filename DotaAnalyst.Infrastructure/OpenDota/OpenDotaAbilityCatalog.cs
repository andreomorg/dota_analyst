using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Infrastructure.Catalogs;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.OpenDota;

// One snapshot keeps the constants' patch beside the abilities and kits in every cache layer.
public sealed class OpenDotaAbilityCatalog(OpenDotaClient client, IMemoryCache cache, ICatalogStore store,
    CatalogLoadLocks locks, TimeProvider timeProvider, ILogger<OpenDotaAbilityCatalog> logger)
    : LayeredCatalog<OpenDotaAbilityCatalog.Snapshot>(cache, store, locks, timeProvider, logger), IAbilityCatalog
{
    public sealed record Snapshot(int Version, string? Patch, IReadOnlyDictionary<string, AbilityDetails> Abilities,
        IReadOnlyDictionary<int, HeroKit> Kits);
    // Version 2 (21/10/2026): innate flag, lore, base damage, percent values, facet abilities, talent text and
    // Aghanim's Scepter/Shard. A saved version 1 counts as old and is reloaded (or still used if OpenDota is down).
    private const int Version = 2;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static Snapshot Empty => new(Version, null, new Dictionary<string, AbilityDetails>(), new Dictionary<int, HeroKit>());
    protected override string CatalogName => "ability_kits";
    private async Task<Snapshot> ReadAsync(CancellationToken ct) => (await GetAsync(ct)).GetValueOrDefault(1) ?? Empty;
    public async Task<IReadOnlyDictionary<string, AbilityDetails>> GetAbilitiesAsync(CancellationToken cancellationToken) =>
        (await ReadAsync(cancellationToken)).Abilities;
    public async Task<IReadOnlyDictionary<int, HeroKit>> GetKitsAsync(CancellationToken cancellationToken) =>
        (await ReadAsync(cancellationToken)).Kits;
    public async Task<string?> GetPatchAsync(CancellationToken cancellationToken) => (await ReadAsync(cancellationToken)).Patch;

    protected override async Task<IReadOnlyDictionary<int, Snapshot>> LoadFromOpenDotaAsync(CancellationToken cancellationToken)
    {
        var abilitiesTask = client.GetAbilitiesAsync(cancellationToken);
        var kitsTask = client.GetHeroAbilitiesAsync(cancellationToken);
        var heroesTask = client.GetHeroesAsync(cancellationToken);
        var patchesTask = client.GetPatchesAsync(cancellationToken);
        var aghanimTask = AghanimAsync(cancellationToken);
        await Task.WhenAll(abilitiesTask, kitsTask, heroesTask, patchesTask, aghanimTask);
        var abilities = (await abilitiesTask).Where(p => p.Value is not null)
            .ToDictionary(p => p.Key, p => p.Value.ToDetails(p.Key));
        var talentNames = abilities.Where(p => p.Key.StartsWith("special_bonus_", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value.Name);
        var aghanim = (await aghanimTask).GroupBy(a => a.HeroId).ToDictionary(g => g.Key, g => g.First());
        var kits = new Dictionary<int, HeroKit>();
        var source = await kitsTask;
        foreach (var hero in await heroesTask)
        {
            if (hero.Name is { } name && source.TryGetValue(name, out var data) && data is not null)
                kits[hero.Id] = data.ToKit(hero.Id, talentNames, aghanim.GetValueOrDefault(hero.Id));
        }
        if (abilities.Count == 0 || kits.Count == 0 || (await patchesTask) is not [.., { Name: { Length: > 0 } patch }])
            return new Dictionary<int, Snapshot>();
        return new Dictionary<int, Snapshot> { [1] = new(Version, patch, abilities, kits) };
    }

    // Aghanim's descriptions are extra: without them the kit is still complete enough to save.
    private async Task<IReadOnlyList<OpenDotaAghanim>> AghanimAsync(CancellationToken cancellationToken)
    {
        try { return await client.GetAghanimDescriptionsAsync(cancellationToken); }
        catch (DataSourceUnavailableException) { return []; }
    }
    protected override Snapshot Create(int id, string name, string? details) => Parse(details) ?? Empty;
    protected override string NameOf(Snapshot entry) => "current";
    protected override string DetailsOf(Snapshot entry) => JsonSerializer.Serialize(entry, Options);
    protected override bool IsComplete(SavedCatalog saved) => saved.Names.ContainsKey(1)
        && Parse(saved.Details.GetValueOrDefault(1)) is { Version: Version, Patch: not null, Abilities.Count: > 0, Kits.Count: > 0 };
    private static Snapshot? Parse(string? json)
    {
        try
        {
            var snapshot = json is null ? null : JsonSerializer.Deserialize<Snapshot>(json, Options);
            return snapshot is { Version: 1 or Version, Abilities: not null, Kits: not null }
                && snapshot.Abilities.Values.All(a => a is
                {
                    Key: not null, Name: not null, Behavior: not null,
                    TargetTypes: not null, Cooldown: not null, ManaCost: not null, Values: not null
                }
                    && a.Values.All(v => v is { Levels: not null }))
                && snapshot.Kits.Values.All(k => k is { Abilities: not null, Talents: not null, Facets: not null })
                ? snapshot : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException) { return null; }
    }
}
