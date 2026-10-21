using DotaAnalyst.Application.Abstractions;

namespace DotaAnalyst.Application.Catalogs;

public sealed class GetHeroKitHandler(IHeroCatalog heroes, IAbilityCatalog abilities)
{
    public async Task<HeroKitResponse?> HandleAsync(int heroId, CancellationToken cancellationToken)
    {
        if (!(await heroes.GetHeroesAsync(cancellationToken)).ContainsKey(heroId)) return null;
        var kits = await abilities.GetKitsAsync(cancellationToken);
        var details = await abilities.GetAbilitiesAsync(cancellationToken);
        var patch = await abilities.GetPatchAsync(cancellationToken);
        if (!kits.TryGetValue(heroId, out var kit)) return new(heroId, patch, [], [], []);
        return new(heroId, patch, kit.Abilities.Where(details.ContainsKey).Select(key => KitAbility.From(details[key])).ToArray(),
            kit.Talents.Select(t => new KitTalent(t.Key, t.Tier)).ToArray(),
            kit.Facets.Select(f => new KitFacet(f.Id, f.Key, f.Title, f.Description, f.Deprecated)).ToArray());
    }
}
