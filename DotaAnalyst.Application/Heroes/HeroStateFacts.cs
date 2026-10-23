using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Heroes;

/// <summary>A hero's state at one exact second, each source kept apart (work/data-flow-audit D14):
/// Visible — what the player could see of their own hero (position, health, mana, gold, invisible);
/// ReplicatedTotals — the game's replicated values (armor/magic resistance/move speed without items and buffs; damage and
/// attributes with bonuses: see HeroCombatStats); Components — the newer replicated components (base attributes, base
/// damage and bonus, BAT, attack range, the raw attack-capability enum — not turned into melee/ranged —, think regen);
/// Nominal — the build catalog's configuration, never a match value; HiddenSeenByEnemies — whether the enemy team saw the
/// hero (the fog of war: omniscient, never a visible fact). Effective defense (armor with items, damage reduction...) is
/// never computed here: unknown. Null parts: the replay or catalog does not have them.</summary>
public sealed record HeroStateFacts(
    string Status, int HeroId, int Seconds, string CatalogScope, string? BuildId,
    HeroVisibleState? Visible, HeroCombatStats? ReplicatedTotals, HeroCombatDetails? Components, HeroProfile? Nominal,
    bool? HiddenSeenByEnemies)
{
    // Effective defensive values are never derived from these parts (no bonus is added twice, none invented).
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Instance property is part of the serialized JSON contract.")]
    public bool EffectiveDefenseKnown => false;

    public static HeroStateFacts From(HeroStateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var sample = result.Sample;
        var visible = sample is { } at
            ? new HeroVisibleState(at.X, at.Y, at.Facing, at.Vitals?.Health, at.Vitals?.MaxHealth, at.Vitals?.Mana,
                at.Vitals?.MaxMana, at.Vitals?.Gold, at.Vitals?.Invisible)
            : null;
        return new HeroStateFacts(result.Status, result.HeroId, result.Seconds, result.CatalogScope, result.Build?.Id,
            visible, sample?.Stats, sample?.CombatDetails, result.Nominal?.Profile, sample?.Vitals?.VisibleToEnemies);
    }
}

/// <summary>What the hero's own player sees of it at the second: position and facing, health, mana, gold and whether it
/// is invisible (each null when the replay does not have it).</summary>
public sealed record HeroVisibleState(
    int X, int Y, int? Facing, int? Health, int? MaxHealth, int? Mana, int? MaxMana, int? Gold, bool? Invisible);
