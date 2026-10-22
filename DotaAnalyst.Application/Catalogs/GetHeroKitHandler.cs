using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;

namespace DotaAnalyst.Application.Catalogs;

public sealed class GetHeroKitHandler(IHeroCatalog heroes, IAbilityCatalog abilities)
{
    public async Task<HeroKitResponse?> HandleAsync(int heroId, CancellationToken cancellationToken)
    {
        if (!(await heroes.GetHeroesAsync(cancellationToken)).ContainsKey(heroId)) return null;
        var kits = await abilities.GetKitsAsync(cancellationToken);
        var details = await abilities.GetAbilitiesAsync(cancellationToken);
        var patch = await abilities.GetPatchAsync(cancellationToken);
        if (!kits.TryGetValue(heroId, out var kit)) return new(heroId, patch, [], [], [], [], null, null);
        var facetAbilities = kit.Facets.SelectMany(f => f.Abilities).Distinct().Except(kit.Abilities);
        return new(heroId, patch, Abilities(kit.Abilities, details),
            kit.Talents.Select(t => new KitTalent(t.Key, t.Tier, t.Name)).ToArray(),
            kit.Facets.Select(f => new KitFacet(f.Id, f.Key, f.Title, f.Description, f.Deprecated, f.Icon, f.Color,
                f.Abilities)).ToArray(),
            Abilities(facetAbilities, details), Upgrade(kit.Scepter), Upgrade(kit.Shard));
    }

    private static KitAbility[] Abilities(IEnumerable<string> keys, IReadOnlyDictionary<string, AbilityDetails> details) =>
        keys.Where(details.ContainsKey).Select(key => KitAbility.From(details[key])).ToArray();

    private static KitAghanimUpgrade? Upgrade(AghanimUpgrade? upgrade) =>
        upgrade is null ? null : new(upgrade.Description, upgrade.AbilityName, upgrade.IsNewAbility);
}
