using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Catalogs;

/// <summary>A hero's current kit. Empty lists mean unavailable catalog data.</summary>
/// <param name="HeroId">Hero identifier.</param>
/// <param name="Patch">Constants patch name; null when unavailable.</param>
/// <param name="Abilities">Abilities in game order, excluding hidden placeholders; innate ones flagged.</param>
/// <param name="Talents">Talent tree entries.</param>
/// <param name="Facets">Facets, including deprecated entries.</param>
/// <param name="FacetAbilities">Abilities granted by facets that are not in the main list.</param>
/// <param name="Scepter">What Aghanim's Scepter does; null when the hero has none or it is unknown.</param>
/// <param name="Shard">What Aghanim's Shard does; null when the hero has none or it is unknown.</param>
public sealed record HeroKitResponse(int HeroId, string? Patch, IReadOnlyList<KitAbility> Abilities,
    IReadOnlyList<KitTalent> Talents, IReadOnlyList<KitFacet> Facets, IReadOnlyList<KitAbility> FacetAbilities,
    KitAghanimUpgrade? Scepter, KitAghanimUpgrade? Shard);
