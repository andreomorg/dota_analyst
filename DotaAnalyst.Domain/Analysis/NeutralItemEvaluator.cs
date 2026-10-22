using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// One craft of a hero, with how long after its tier opened it came (null without the build's rules).
public sealed record EvaluatedCraft(NeutralCraft Craft, int? DelaySeconds);

// What a hero did with neutral items: its crafts (with the delay after each tier opened), the tiers that opened during
// the match and were never crafted, and its Madstones.
public sealed record HeroNeutrals(
    int HeroId,
    IReadOnlyList<EvaluatedCraft> Crafts,
    IReadOnlyList<int> TiersSkipped,
    double? AverageDelaySeconds,
    NeutralProgress? Madstones);

// Domain service: reads the neutral crafts of a match against the build's tier rules. A tier "skipped" is one that
// opened at least a craft's worth of time before the end (the hero could have crafted it) and has no craft; a craft
// of a higher tier does not cover a lower one (each tier is its own craft).
public static class NeutralItemEvaluator
{
    // A tier opened less than this before the end of the match is not counted as skipped: there was no time.
    public const int GraceSeconds = 120;

    public static IReadOnlyList<HeroNeutrals> Evaluate(
        IReadOnlyList<int> heroIds, IReadOnlyList<NeutralCraft> crafts, IReadOnlyList<NeutralProgress>? progress,
        NeutralRules? rules, int matchEndsAtSeconds)
    {
        ArgumentNullException.ThrowIfNull(heroIds);
        ArgumentNullException.ThrowIfNull(crafts);
        return heroIds.Select(heroId =>
        {
            var own = crafts.Where(craft => craft.HeroId == heroId).OrderBy(craft => craft.Seconds).ToList();
            var evaluated = own.Select(craft => new EvaluatedCraft(craft,
                rules?.Tiers.FirstOrDefault(tier => tier.Tier == craft.Tier) is { } tier
                    ? Math.Max(0, (int)Math.Round(craft.Seconds) - tier.OpensAtSeconds) : null)).ToList();
            var skipped = rules is null ? []
                : rules.Tiers.Where(tier => tier.OpensAtSeconds + GraceSeconds <= matchEndsAtSeconds && own.All(craft => craft.Tier != tier.Tier))
                    .Select(tier => tier.Tier).ToList();
            var delays = evaluated.Select(craft => craft.DelaySeconds).OfType<int>().ToList();
            return new HeroNeutrals(heroId, evaluated, skipped, delays.Count == 0 ? null : Math.Round(delays.Average(), 1),
                progress?.FirstOrDefault(entry => entry.HeroId == heroId));
        }).ToList();
    }
}
