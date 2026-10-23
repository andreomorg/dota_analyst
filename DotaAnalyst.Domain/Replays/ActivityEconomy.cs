using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

public enum EconomyResource
{
    Gold,
    Experience,
}

// What a gold/XP change came from, grouped from Valve's raw reason (EDOTA_ModifyGold_Reason / ModifyXP_Reason): only
// the reasons whose meaning is stable across patches; the rest is Other.
public enum EconomySource
{
    Creeps,
    Neutrals,
    Heroes,
    Objectives,
    BountyRune,
    Death,
    Other,
}

// Gold or XP a hero got (or lost: negative) for one reason in the EconomyBucket.Length seconds starting at Seconds.
public sealed record EconomyBucket(int HeroId, int Seconds, EconomyResource Resource, EconomySource Source, int Amount)
{
    public const int Length = 2;

    public bool EndsBy(int second) => Seconds + Length <= second;

    // Gold: 1 death, 11 building, 12 hero kill, 13 creep, 14 neutral, 15 Roshan, 17 bounty rune. XP: 1 hero, 2 creep,
    // 3 Roshan. Neutral creeps give XP as creeps (reason 2), so XP has no Neutrals.
    public static EconomySource SourceOf(EconomyResource resource, int? reason) => (resource, reason) switch
    {
        (EconomyResource.Gold, 1) => EconomySource.Death,
        (EconomyResource.Gold, 11) or (EconomyResource.Gold, 15) => EconomySource.Objectives,
        (EconomyResource.Gold, 12) => EconomySource.Heroes,
        (EconomyResource.Gold, 13) => EconomySource.Creeps,
        (EconomyResource.Gold, 14) => EconomySource.Neutrals,
        (EconomyResource.Gold, 17) => EconomySource.BountyRune,
        (EconomyResource.Experience, 1) => EconomySource.Heroes,
        (EconomyResource.Experience, 2) => EconomySource.Creeps,
        (EconomyResource.Experience, 3) => EconomySource.Objectives,
        _ => EconomySource.Other,
    };
}
