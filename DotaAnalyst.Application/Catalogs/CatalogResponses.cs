using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Catalogs;

/// <summary>An attribute's numeric levels or original nonnumeric text.</summary>
/// <param name="Key">Internal attribute key.</param>
/// <param name="Header">Display label from the game.</param>
/// <param name="Levels">Numbers in level order; empty for nonnumeric values.</param>
/// <param name="Text">Original nonnumeric value, if any.</param>
/// <param name="IsPercent">Whether the game writes the numbers as percentages ("15%").</param>
/// <param name="Generated">Whether the value was filled in from the ability's own fields instead of the tooltip.</param>
/// <param name="Kind">What the value measures: damage dealt, healing, health the caster pays, or other.</param>
public sealed record CatalogValue(string Key, string? Header, IReadOnlyList<double> Levels, string? Text,
    bool IsPercent, bool Generated, AbilityValueKind Kind)
{
    internal static CatalogValue From(AbilityValue value) =>
        new(value.Key, value.Header, value.Levels, value.Text, value.IsPercent, value.Generated, value.Kind);
}

/// <summary>A current-patch ability.</summary>
/// <param name="Key">Internal ability key.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Game description, when available.</param>
/// <param name="Behavior">Casting behaviors.</param>
/// <param name="TargetTeam">Target allegiance; none when unspecified.</param>
/// <param name="TargetTypes">Target unit types.</param>
/// <param name="DamageType">Damage type; none when unspecified.</param>
/// <param name="PiercesSpellImmunity">Whether it pierces immunity; null when unknown.</param>
/// <param name="Dispellable">Dispel requirement.</param>
/// <param name="Cooldown">Cooldown seconds per level.</param>
/// <param name="ManaCost">Mana cost per level.</param>
/// <param name="Values">Attributes per level.</param>
/// <param name="IsInnate">Whether the hero has it from level 1 without spending points.</param>
/// <param name="Lore">Flavor text, when available.</param>
/// <param name="BaseDamage">Damage per level kept outside the attributes; empty when none.</param>
public sealed record KitAbility(string Key, string Name, string? Description, IReadOnlyList<string> Behavior,
    AbilityTargetTeam TargetTeam, IReadOnlyList<string> TargetTypes, AbilityDamageType DamageType,
    bool? PiercesSpellImmunity, Dispellable Dispellable, IReadOnlyList<double> Cooldown,
    IReadOnlyList<double> ManaCost, IReadOnlyList<CatalogValue> Values, bool IsInnate, string? Lore,
    IReadOnlyList<double> BaseDamage)
{
    internal static KitAbility From(AbilityDetails a) => new(a.Key, a.Name, a.Description, a.Behavior, a.TargetTeam,
        a.TargetTypes, a.DamageType, a.PiercesSpellImmunity, a.Dispellable, a.Cooldown, a.ManaCost,
        a.Values.Select(CatalogValue.From).ToArray(), a.IsInnate, a.Lore, a.BaseDamage);
}

/// <summary>A talent in the hero's talent tree.</summary>
/// <param name="Key">Internal talent name.</param>
/// <param name="Tier">Game tier, normally 1 (level 10) through 4 (level 25).</param>
/// <param name="Name">Game text, with numbers as {s:...} placeholders; null when unavailable.</param>
public sealed record KitTalent(string Key, int Tier, string? Name);

/// <summary>A hero facet from the constants.</summary>
/// <param name="Id">Facet identifier.</param>
/// <param name="Key">Internal facet name.</param>
/// <param name="Title">Display title.</param>
/// <param name="Description">Game description, with numbers as {s:...} placeholders.</param>
/// <param name="Deprecated">Whether the facet is no longer pickable.</param>
/// <param name="Icon">Game icon name, when available.</param>
/// <param name="Color">Game color name, when available.</param>
/// <param name="Abilities">Keys of the abilities the facet grants.</param>
public sealed record KitFacet(int Id, string Key, string? Title, string? Description, bool Deprecated,
    string? Icon, string? Color, IReadOnlyList<string> Abilities);

/// <summary>What Aghanim's Scepter or Shard does for the hero.</summary>
/// <param name="Description">Game description of the upgrade.</param>
/// <param name="AbilityName">Display name of the ability it upgrades or grants, when given.</param>
/// <param name="IsNewAbility">Whether it grants a new ability.</param>
public sealed record KitAghanimUpgrade(string Description, string? AbilityName, bool IsNewAbility);

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

/// <summary>An item's active or passive effect.</summary>
/// <param name="Kind">Effect kind.</param>
/// <param name="Title">Effect title.</param>
/// <param name="Description">Game description.</param>
public sealed record CatalogItemAbility(ItemAbilityKind Kind, string? Title, string? Description);

/// <summary>The full game data available for an item.</summary>
/// <param name="Id">Numeric item identifier.</param>
/// <param name="Key">Internal item key without item_.</param>
/// <param name="Name">Display name.</param>
/// <param name="Cost">Gold cost.</param>
/// <param name="Components">Component keys.</param>
/// <param name="IsConsumable">Whether consumed rather than used in a build.</param>
/// <param name="IsRecipe">Whether this is a recipe.</param>
/// <param name="Abilities">Active and passive effects.</param>
/// <param name="Behavior">Casting behaviors.</param>
/// <param name="Cooldown">Cooldown seconds; null when unavailable.</param>
/// <param name="ManaCost">Mana cost; null when unavailable.</param>
/// <param name="Values">Item attributes per level.</param>
/// <param name="HasCharges">Whether the item has charges.</param>
public sealed record ItemResponse(int Id, string Key, string Name, int Cost, IReadOnlyList<string> Components,
    bool IsConsumable, bool IsRecipe, IReadOnlyList<CatalogItemAbility> Abilities, IReadOnlyList<string> Behavior,
    double? Cooldown, double? ManaCost, IReadOnlyList<CatalogValue> Values, bool HasCharges);
