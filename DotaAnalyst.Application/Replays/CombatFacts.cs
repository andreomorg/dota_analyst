using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

// Availability of a family of facts (work/data-flow-audit/contracts.md): available, partial (some of it), unavailable
// (the document lacks it, or it is malformed). Zero is only reported when the family exists to back it.
public enum FactAvailability
{
    Available,
    Partial,
    Unavailable,
}

/// <summary>A hero's combat in a closed window [From, To] of the game clock, from the 2-second buckets that ended by To
/// (a bucket still running at To never counts, nor a share of it). Dealt and taken are to/from enemy heroes, by damage
/// type; healing received and done both include self-healing (never add them up). Damage/Healing: availability of each
/// bucket family; From: the first bucket counted starts at or after it; To: the anchor, by which the last one ended.</summary>
public sealed record CombatWindowFacts(
    FactAvailability Damage, FactAvailability Healing, int From, double To,
    int? PhysicalDealt, int? MagicalDealt, int? PureDealt, int? PhysicalTaken, int? MagicalTaken, int? PureTaken,
    int? HealingReceived, int? HealingDone);

/// <summary>A hero's healing over the whole match (a final fact), from the aggregated rows: to itself, to allied heroes, to
/// anything else (illusions, summons, units), and received from others. Self-healing is counted once.</summary>
public sealed record HealingFinalFacts(
    FactAvailability Availability, int? SelfHealing, int? DoneToAllies, int? DoneToOthers, int? ReceivedFromOthers,
    IReadOnlyList<HealingByInflictor> ByInflictor);

/// <summary>Healing the hero did with one ability or item (null: regeneration or unknown), whole match.</summary>
public sealed record HealingByInflictor(string? Inflictor, int Amount);

// Read-only facts about combat for the internal factual consumer (D18). Never reaches past the anchor: windows use only
// buckets that ended by it; the whole-match healing is a separate final fact.
public static class CombatFacts
{
    public static CombatWindowFacts Window(ReplayActivity? activity, int heroId, int from, double to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfLessThan(to, from);

        // The buckets are sparse (only those with something in them): with the family present, none is zero.
        var typed = activity?.TypedDamage?.Where(bucket => bucket.HeroId == heroId && bucket.Seconds >= from && bucket.EndsBy((int)Math.Floor(to))).ToList();
        var healed = activity?.Healing?.Where(bucket => bucket.HeroId == heroId && bucket.Seconds >= from && bucket.EndsBy((int)Math.Floor(to))).ToList();
        int? Sum<T>(List<T>? buckets, Func<T, int> amount) => buckets?.Sum(amount);

        return new CombatWindowFacts(
            typed is null ? FactAvailability.Unavailable : FactAvailability.Available,
            healed is null ? FactAvailability.Unavailable : FactAvailability.Available,
            from, to,
            Sum(typed, bucket => bucket.PhysicalTo), Sum(typed, bucket => bucket.MagicalTo), Sum(typed, bucket => bucket.PureTo),
            Sum(typed, bucket => bucket.PhysicalFrom), Sum(typed, bucket => bucket.MagicalFrom), Sum(typed, bucket => bucket.PureFrom),
            Sum(healed, bucket => bucket.Received), Sum(healed, bucket => bucket.Done));
    }

    // allies: the hero IDs of the hero's team (itself included or not: it is told apart by SelfHeal).
    public static HealingFinalFacts FinalHealing(ReplayAdditionalFacts? facts, int heroId, IReadOnlySet<int> allies)
    {
        ArgumentNullException.ThrowIfNull(allies);
        if (facts?.Healing is not { } rows)
        {
            return new HealingFinalFacts(FactAvailability.Unavailable, null, null, null, null, []);
        }

        var done = rows.Where(row => row.SourceHeroId == heroId).ToList();
        var self = done.Where(row => row.SelfHeal).Sum(row => row.Amount);
        var toAllies = done.Where(row => !row.SelfHeal && row.TargetHeroId is { } target && allies.Contains(target)
            && row.TargetKind == DamageRow.HeroKind).Sum(row => row.Amount);
        var toOthers = done.Sum(row => row.Amount) - self - toAllies;
        var received = rows.Where(row => row.TargetHeroId == heroId && row.TargetKind == DamageRow.HeroKind && !row.SelfHeal)
            .Sum(row => row.Amount);
        var byInflictor = done.GroupBy(row => row.Inflictor)
            .Select(group => new HealingByInflictor(group.Key, group.Sum(row => row.Amount)))
            .OrderByDescending(entry => entry.Amount)
            .ToList();
        return new HealingFinalFacts(FactAvailability.Available, self, toAllies, toOthers, received, byInflictor);
    }
}
