namespace DotaAnalyst.Domain.Abilities;

// A value of an ability or item attribute: one number per level (a single value applies to every level), or the raw
// text when the game data is not a number list ("9 / 8 / 7" is read as three levels). IsPercent: the game wrote the
// numbers with "%" ("15% / 20%" is 15 and 20). Generated: filled in by the game data from the ability's own fields
// (cast range, cast time...) rather than shown on the tooltip.
public sealed record AbilityValue(string Key, string? Header, IReadOnlyList<double> Levels, string? Text = null,
    bool IsPercent = false, bool Generated = false, ValueRules? Rules = null)
{
    // The value at a level (1-based), the last one when the level is beyond the list; null without numbers.
    public double? At(int level) => Levels.Count == 0 ? null : Levels[Math.Clamp(level, 1, Levels.Count) - 1];

    public AbilityValueKind Kind => AbilityValueKinds.Of(this);
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
// IsInnate: the hero has it from level 1 without spending points (patch 7.36+). Lore: the flavor text. BaseDamage: the
// ability's own damage per level when the game data keeps it outside the attributes (dmg, mostly creep abilities).
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
    IReadOnlyList<AbilityValue> Values,
    bool IsInnate = false,
    string? Lore = null,
    IReadOnlyList<double>? BaseDamage = null,
    AbilityProfile? Profile = null)
{
    public IReadOnlyList<double> BaseDamage { get; init; } = BaseDamage ?? [];

    // Value keys that are the size of the area the ability hits.
    private static readonly string[] AreaKeys = ["radius", "area_of_effect", "aoe", "width"];

    // Whom it acts on. Enemies: it targets them, or it targets no unit and deals damage. Allies: it targets them
    // (Friendly or Both). Itself: it has no unit target and is not an attack on others (buffs, toggles, most passives).
    public bool AffectsEnemies => TargetTeam is AbilityTargetTeam.Enemy or AbilityTargetTeam.Both
        || (TargetTeam is AbilityTargetTeam.None or AbilityTargetTeam.Custom && DamageType != AbilityDamageType.None);

    public bool AffectsAllies => TargetTeam is AbilityTargetTeam.Ally or AbilityTargetTeam.Both;

    public bool AffectsSelf => TargetTeam is AbilityTargetTeam.None || Profile?.Has("CAN_SELF_CAST") == true;

    // Hits an area: the game flags it AOE, or it has an area size (radius, width...) above zero.
    public bool IsAreaOfEffect => Profile?.Has("AOE") == true
        || Behavior.Any(behavior => behavior.Equals("AOE", StringComparison.OrdinalIgnoreCase))
        || Values.Any(value => IsAreaKey(value.Key) && value.Levels.Any(level => level > 0));

    // Cast on one unit and nothing around it.
    public bool IsSingleTarget => !IsAreaOfEffect && (Profile?.Has("UNIT_TARGET")
        ?? Behavior.Any(behavior => behavior.Equals("Unit Target", StringComparison.OrdinalIgnoreCase)));

    public bool IsChanneled => Profile?.Has("CHANNELLED")
        ?? Behavior.Any(behavior => behavior.StartsWith("Channel", StringComparison.OrdinalIgnoreCase));

    // Range and area at a level (1-based), with no talent, Scepter or Shard; null when the ability has none.
    public double? CastRangeAt(int level) =>
        At(Profile?.CastRange is { Count: > 0 } range ? range : null, level) ?? Value("abilitycastrange")?.At(level);

    public double? RadiusAt(int level) => Values.FirstOrDefault(value => IsAreaKey(value.Key) && value.Levels.Count > 0)?.At(level);

    // The ultimate as the game declares it; without the game files, three levels of cooldown or mana (a heuristic).
    public bool IsUltimate => Profile?.IsUltimate ?? LooksLikeUltimate;

    private static bool IsAreaKey(string key) =>
        AreaKeys.Contains(key, StringComparer.OrdinalIgnoreCase) || key.EndsWith("_radius", StringComparison.OrdinalIgnoreCase);

    private static double? At(IReadOnlyList<double>? levels, int level) =>
        levels is { Count: > 0 } ? levels[Math.Clamp(level, 1, levels.Count) - 1] : null;

    public bool IsPassive => Behavior.Any(behavior => behavior.Equals("Passive", StringComparison.OrdinalIgnoreCase));

    // Mana cost at a level (1-based, the last one beyond the list); null when the ability costs no mana.
    public double? ManaCostAt(int level) => ManaCost.Count == 0 ? null : ManaCost[Math.Clamp(level, 1, ManaCost.Count) - 1];

    // The attributes that measure damage dealt, health given back or health the caster pays (AbilityValueKinds).
    public IReadOnlyList<AbilityValue> ValuesOf(AbilityValueKind kind) => Values.Where(value => value.Kind == kind).ToArray();

    // The ability's headline damage or healing at a level, in hit points (null when it has none): the base damage, else
    // the attribute named exactly "damage"/"heal", else the first flat one of that kind that is not per second. A
    // heuristic for the models (one number per spell), not the full tooltip: percentages and damage over time are
    // left out because they depend on the target or on how long it stays.
    // bonuses: what applies to the hero now (its learned talents by key, ValueBonus.Scepter, ValueBonus.Shard); values
    // that only exist with Scepter/Shard are left out without it. Null = the base values only.
    public double? MainAmount(AbilityValueKind kind, int level, IReadOnlySet<string>? bonuses = null)
    {
        if (kind == AbilityValueKind.Damage && BaseDamage.Count > 0)
        {
            return BaseDamage[Math.Clamp(level, 1, BaseDamage.Count) - 1];
        }

        var flat = ValuesOf(kind).Where(value => !value.IsPercent && value.Levels.Count > 0 && Applies(value, bonuses)).ToList();
        var exact = kind == AbilityValueKind.Damage ? "damage" : "heal";
        var main = flat.FirstOrDefault(value => value.Key.Equals(exact, StringComparison.OrdinalIgnoreCase))
            ?? flat.FirstOrDefault(value => !(value.Header ?? value.Key).Contains("SECOND", StringComparison.OrdinalIgnoreCase));
        return WithBonuses(main, level, bonuses);
    }

    // The cast range at a level with the bonuses that apply (a talent's +600 range), null when it has none.
    public double? CastRangeAt(int level, IReadOnlySet<string>? bonuses) =>
        Value("abilitycastrange") is { Levels.Count: > 0 } range ? WithBonuses(range, level, bonuses) : CastRangeAt(level);

    // The area size at a level with the bonuses that apply (Scepter-only areas count with Scepter).
    public double? RadiusAt(int level, IReadOnlySet<string>? bonuses) =>
        WithBonuses(Values.FirstOrDefault(value => IsAreaKey(value.Key) && Applies(value, bonuses)
            && (value.Levels.Count > 0 || value.Rules?.Bonuses.Count > 0)), level, bonuses);

    // A value at a level after every bonus whose source applies; a value with no levels of its own starts at 0 (one
    // that only a bonus gives, "special_bonus_shard" "2").
    public static double? WithBonuses(AbilityValue? value, int level, IReadOnlySet<string>? bonuses)
    {
        if (value is null)
        {
            return null;
        }

        var result = value.At(level);
        foreach (var bonus in value.Rules?.Bonuses ?? [])
        {
            if (bonuses?.Contains(bonus.Source) == true)
            {
                result = bonus.Apply(result ?? 0, level);
            }
        }

        return result;
    }

    private static bool Applies(AbilityValue value, IReadOnlySet<string>? bonuses) =>
        value.Rules is not { } rules
        || ((!rules.RequiresScepter || bonuses?.Contains(ValueBonus.Scepter) == true)
            && (!rules.RequiresShard || bonuses?.Contains(ValueBonus.Shard) == true));

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
        && IsInnate == other.IsInnate && Lore == other.Lore && BaseDamage.SequenceEqual(other.BaseDamage)
        && Values.Select(Comparable).SequenceEqual(other.Values.Select(Comparable));

    private static (string, string?, string?, string, bool, bool) Comparable(AbilityValue value) =>
        (value.Key, value.Header, value.Text, string.Join(';', value.Levels), value.IsPercent, value.Generated);

    public override int GetHashCode() => HashCode.Combine(Key, Name);
}

// A hero's talent: its internal key (special_bonus_unique_lion_6), its tier (1 = level 10, 2 = 15, 3 = 20, 4 = 25) and
// its text as the game writes it. The game data keeps the number as a placeholder ("+{s:bonus_movespeed}% Mana Drain
// Slow"): the value lives in the game files, not in the constants.
public sealed record Talent(string Key, int Tier, string? Name = null);

// A hero's facet (patch 7.36+): internal key, title, description (numbers as {s:...} placeholders), the game's icon
// and color names, and the abilities the facet gives the hero; Deprecated ones are no longer pickable.
public sealed record Facet(int Id, string Key, string? Title, string? Description, bool Deprecated,
    string? Icon = null, string? Color = null, IReadOnlyList<string>? Abilities = null)
{
    public IReadOnlyList<string> Abilities { get; init; } = Abilities ?? [];

    public bool Equals(Facet? other) =>
        other is not null && Id == other.Id && Key == other.Key && Title == other.Title && Description == other.Description
        && Deprecated == other.Deprecated && Icon == other.Icon && Color == other.Color && Abilities.SequenceEqual(other.Abilities);

    public override int GetHashCode() => HashCode.Combine(Id, Key);
}

// What Aghanim's Scepter or Shard does for a hero: the description, the ability it changes (by display name, as the
// game data gives it) and whether it grants a new ability instead.
public sealed record AghanimUpgrade(string Description, string? AbilityName, bool IsNewAbility);

// Value object: a hero's kit in the current patch — its abilities in the game's order (hidden placeholders such as
// generic_hidden left out; innate abilities included, flagged on AbilityDetails), its talents, its facets and what
// Aghanim's Scepter and Shard do (null when the hero has no upgrade or the data is missing).
public sealed record HeroKit(int HeroId, IReadOnlyList<string> Abilities, IReadOnlyList<Talent> Talents, IReadOnlyList<Facet> Facets,
    AghanimUpgrade? Scepter = null, AghanimUpgrade? Shard = null)
{
    public bool Equals(HeroKit? other) =>
        other is not null && HeroId == other.HeroId && Abilities.SequenceEqual(other.Abilities)
        && Talents.SequenceEqual(other.Talents) && Facets.SequenceEqual(other.Facets)
        && Scepter == other.Scepter && Shard == other.Shard;

    public override int GetHashCode() => HashCode.Combine(HeroId, Abilities.Count);
}
