namespace DotaAnalyst.Domain.Abilities;

// A value of an ability or item attribute: one number per level (a single value applies to every level), or the raw
// text when the game data is not a number list ("9 / 8 / 7" is read as three levels).
public sealed record AbilityValue(string Key, string? Header, IReadOnlyList<double> Levels, string? Text = null)
{
    // The value at a level (1-based), the last one when the level is beyond the list; null without numbers.
    public double? At(int level) => Levels.Count == 0 ? null : Levels[Math.Clamp(level, 1, Levels.Count) - 1];
}

public enum AbilityTargetTeam
{
    None,
    Enemy,
    Ally,
    Both,
    Custom,
}

public enum AbilityDamageType
{
    None,
    Physical,
    Magical,
    Pure,
}

// How the game removes the ability's debuff: not at all, only by strong dispels, or by any dispel.
public enum Dispellable
{
    Unknown,
    No,
    StrongOnly,
    Yes,
}

// Value object: what an ability is, from the game data of the current patch (OpenDota constants, built from the game
// files): its internal key (lion_impale), name, description, behavior ("Unit Target", "Passive", "Channeled"...),
// whom it targets, damage type, whether it pierces spell immunity, how its debuff is dispelled, cooldown and mana cost
// per level, and every attribute (damage, duration, radius...) per level. Effects are classified by AbilityEffects.
public sealed record AbilityDetails(
    string Key,
    string Name,
    string? Description,
    IReadOnlyList<string> Behavior,
    AbilityTargetTeam TargetTeam,
    IReadOnlyList<string> TargetTypes,
    AbilityDamageType DamageType,
    bool? PiercesSpellImmunity,
    Dispellable Dispellable,
    IReadOnlyList<double> Cooldown,
    IReadOnlyList<double> ManaCost,
    IReadOnlyList<AbilityValue> Values)
{
    public bool IsPassive => Behavior.Any(behavior => behavior.Equals("Passive", StringComparison.OrdinalIgnoreCase));

    // Ultimates have three levels; basic abilities four (a heuristic over the level lists: the constants do not say).
    public int Levels => Math.Max(Cooldown.Count, Math.Max(ManaCost.Count, Values.Select(value => value.Levels.Count).DefaultIfEmpty(1).Max()));

    public bool LooksLikeUltimate => Cooldown.Count == 3 || ManaCost.Count == 3;

    public AbilityValue? Value(string key) => Values.FirstOrDefault(value => value.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    // A record compares lists by reference: the same data is the same ability.
    public bool Equals(AbilityDetails? other) =>
        other is not null && Key == other.Key && Name == other.Name && Description == other.Description
        && Behavior.SequenceEqual(other.Behavior) && TargetTeam == other.TargetTeam && TargetTypes.SequenceEqual(other.TargetTypes)
        && DamageType == other.DamageType && PiercesSpellImmunity == other.PiercesSpellImmunity && Dispellable == other.Dispellable
        && Cooldown.SequenceEqual(other.Cooldown) && ManaCost.SequenceEqual(other.ManaCost)
        && Values.Select(value => (value.Key, value.Header, value.Text, string.Join(';', value.Levels)))
            .SequenceEqual(other.Values.Select(value => (value.Key, value.Header, value.Text, string.Join(';', value.Levels))));

    public override int GetHashCode() => HashCode.Combine(Key, Name);
}

// A hero's talent: its internal key (special_bonus_unique_lion_6) and its tier (1 = level 10, 2 = 15, 3 = 20, 4 = 25).
public sealed record Talent(string Key, int Tier);

// A hero's facet (patch 7.36+): internal key, title and description; Deprecated ones are no longer pickable.
public sealed record Facet(int Id, string Key, string? Title, string? Description, bool Deprecated);

// Value object: a hero's kit in the current patch — its abilities in the game's order (hidden placeholders such as
// generic_hidden left out), its talents and its facets.
public sealed record HeroKit(int HeroId, IReadOnlyList<string> Abilities, IReadOnlyList<Talent> Talents, IReadOnlyList<Facet> Facets)
{
    public bool Equals(HeroKit? other) =>
        other is not null && HeroId == other.HeroId && Abilities.SequenceEqual(other.Abilities)
        && Talents.SequenceEqual(other.Talents) && Facets.SequenceEqual(other.Facets);

    public override int GetHashCode() => HashCode.Combine(HeroId, Abilities.Count);
}
