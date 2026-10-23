using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Catalogs;

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
