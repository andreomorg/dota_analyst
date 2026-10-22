namespace DotaAnalyst.Domain.Abilities;

// How a bonus changes a value: adds (+10, -2), multiplies (x2) or replaces it (a plain number).
public enum BonusOperation
{
    Add,
    Multiply,
    Replace,
}

// A change to an ability value from a talent (its key, special_bonus_unique_lion_6), Aghanim's Scepter ("scepter") or
// Shard ("shard"), as the game files write it: "+600" on AbilityCastRange is +600 range with that talent.
public sealed record ValueBonus(string Source, BonusOperation Operation, IReadOnlyList<double> Amount)
{
    public const string Scepter = "scepter";
    public const string Shard = "shard";

    public double Apply(double value, int level)
    {
        var amount = Amount.Count == 0 ? 0 : Amount[Math.Clamp(level, 1, Amount.Count) - 1];
        return Operation switch
        {
            BonusOperation.Add => value + amount,
            BonusOperation.Multiply => value * amount,
            _ => amount,
        };
    }
}

// What the game files say about a value beyond its levels: the bonuses on it, whether it only exists with Scepter or
// Shard, and whether area-of-effect increases (talents, items) change it.
public sealed record ValueRules(
    IReadOnlyList<ValueBonus> Bonuses,
    bool RequiresScepter,
    bool RequiresShard,
    bool AffectedByAoeIncrease)
{
    public ValueBonus? BonusFrom(string source) =>
        Bonuses.FirstOrDefault(bonus => bonus.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
}

// Who a spell's immunity rule lets it reach (SpellImmunityType): whether it goes through enemies' (and allies')
// spell/debuff immunity.
public enum SpellImmunity
{
    Unknown,
    EnemiesNo,
    EnemiesYes,
    AlliesYes,
    AlliesNo,
    AlliesYesEnemiesNo,
}

// Value object: everything the game files (patch build) say about an ability that the OpenDota constants do not: every
// behavior flag as the game names it without its prefix (UNIT_TARGET, AOE, CHANNELLED, AUTOCAST, ...), the target flags
// (MAGIC_IMMUNE_ENEMIES, NOT_ANCIENTS, ...), the immunity rule, whether it is the ultimate (AbilityType), its max
// level, how Scepter/Shard relate to it, cast range/cast point/channel/duration/health cost per level, charges, the
// linked ability, whether break disables it, and the translated notes and Scepter/Shard texts.
public sealed record AbilityProfile(
    IReadOnlyList<string> Behaviors,
    IReadOnlyList<string> TargetFlags,
    SpellImmunity SpellImmunity,
    bool? IsUltimate,
    bool IsTalent,
    int? MaxLevel,
    bool IsGrantedByScepter,
    bool IsGrantedByShard,
    bool HasScepterUpgrade,
    bool HasShardUpgrade,
    IReadOnlyList<double> CastRange,
    IReadOnlyList<double> CastPoint,
    IReadOnlyList<double> ChannelTime,
    IReadOnlyList<double> Duration,
    IReadOnlyList<double> HealthCost,
    IReadOnlyList<double> Charges,
    IReadOnlyList<double> ChargeRestoreTime,
    string? LinkedAbility,
    bool? IsBreakable,
    IReadOnlyList<string> Notes,
    string? ScepterDescription,
    string? ShardDescription)
{
    public bool Has(string behavior) => Behaviors.Contains(behavior, StringComparer.OrdinalIgnoreCase);
}
