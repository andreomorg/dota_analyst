using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// What a hero farmed, by kind (ReplayActivity.CreepKills: each creep it got the last hit on): lane creeps, neutrals and
// denies, over the whole match and after the laning phase (from 10:00), when the jungle becomes a real choice.
public sealed record FarmMetrics(int LaneCreeps, int Neutrals, int Denies, int LaneCreepsAfterLaning, int NeutralsAfterLaning);

// Domain service: the FarmMetrics of one hero.
public static class FarmMetricsCalculator
{
    // Null without the creep kills in the document.
    public static FarmMetrics? Calculate(int heroId, IReadOnlyList<CreepKill>? creepKills)
    {
        if (creepKills is null)
        {
            return null;
        }

        var mine = creepKills.Where(kill => kill.HeroId == heroId).ToList();
        var late = mine.Where(kill => kill.Seconds >= ReplayAnalysis.LaningPhaseEndsAtSeconds).ToList();
        return new FarmMetrics(
            mine.Count(kill => kill.Kind == CreepKillKind.Lane),
            mine.Count(kill => kill.Kind == CreepKillKind.Neutral),
            mine.Count(kill => kill.Kind == CreepKillKind.Deny),
            late.Count(kill => kill.Kind == CreepKillKind.Lane),
            late.Count(kill => kill.Kind == CreepKillKind.Neutral));
    }
}
