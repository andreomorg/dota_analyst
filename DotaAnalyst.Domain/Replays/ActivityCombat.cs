using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// Hero damage of one hero in the DamageBucket.Length seconds starting at Seconds (it covers [Seconds, Seconds + Length)):
// dealt to enemy heroes and taken from them.
public sealed record DamageBucket(int HeroId, int Seconds, int ToHeroes, int FromHeroes)
{
    public const int Length = 2;

    // Over by `second`: only these may describe that second without looking ahead.
    public bool EndsBy(int second) => Seconds + Length <= second;
}

// Hero damage of one hero by damage type in the DamageBucket.Length seconds starting at Seconds (X30; damage without a
// type only counts in DamageBucket), and the healing it received and did then (self-healing included).
public sealed record TypedDamageBucket(
    int HeroId, int Seconds, int PhysicalTo, int MagicalTo, int PureTo, int PhysicalFrom, int MagicalFrom, int PureFrom)
{
    public bool EndsBy(int second) => Seconds + DamageBucket.Length <= second;
}

public sealed record HealingBucket(int HeroId, int Seconds, int Received, int Done)
{
    public bool EndsBy(int second) => Seconds + DamageBucket.Length <= second;
}

// The damage one hero (its illusions and summons too) dealt to an enemy hero in the DamageBucket.Length seconds starting
// at Seconds (it covers [Seconds, Seconds + Length)).
public sealed record HeroHit(int Seconds, int AttackerHeroId, int TargetHeroId, int Amount)
{
    public bool EndsBy(int second) => Seconds + DamageBucket.Length <= second;
}

public enum CreepKillKind
{
    Lane,
    Deny,
    Neutral,
}

// A lane creep or neutral a hero got the last hit on, where the hero was then (null: not on the map).
public sealed record CreepKill(int HeroId, double Seconds, CreepKillKind Kind, int? X, int? Y);

// What a debuff does to its target. Disables (stun, hex, silence, root) take control away.
public enum DebuffKind
{
    Stun,
    Hex,
    Silence,
    Root,
    Slow,
    DamageOverTime,
    ArmorReduction,
    Other,
}

// A modifier an enemy player's unit put on a hero, from Start to End (the replay's own times; End null when the replay
// does not say). SourceHeroId null: the replay does not say who.
public sealed record Debuff(double Start, double? End, int? SourceHeroId, int TargetHeroId, string Modifier, DebuffKind Kind)
{
    public bool IsDisable => Kind is DebuffKind.Stun or DebuffKind.Hex or DebuffKind.Silence or DebuffKind.Root;

    // On the hero at this second: started and not removed yet. Only "not removed yet" is read from End, never when it
    // ends (the future); a debuff without an end counts only in the second it started.
    public bool ActiveAt(int second) => Start <= second && (End is { } end ? end > second : second - Start < 1);
}

// What a buff the hero or an ally put on a hero does for it.
public enum BuffKind
{
    Teleport,
    Smoke,
    Invisible,
    MagicImmune,
    // Untargetable or protected from death for a moment (Glimmer fade, Eul's, Aeon Disk, Shallow Grave...).
    Save,
    Heal,
    Speed,
    Other,
}

// A modifier the hero itself or an allied player's unit put on a hero, from Start to End (same reading as Debuff).
public sealed record Buff(double Start, double? End, int? SourceHeroId, int TargetHeroId, string Modifier, BuffKind Kind)
{
    // Put by another hero of the team (null source: the replay does not say, so not an ally's).
    public bool FromAlly => SourceHeroId is { } source && source != TargetHeroId;

    public bool ActiveAt(int second) => Start <= second && (End is { } end ? end > second : second - Start < 1);
}
