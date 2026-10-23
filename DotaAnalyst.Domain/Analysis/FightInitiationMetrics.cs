using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// Who started the team fights a hero was in: the first hero-on-hero hit between the two teams (ReplayActivity.HeroHits,
// 2 s buckets) from LeadInSeconds before the fight's first death to its end.
// - TeamStarted: fights whose first hits all came from the hero's team; HeroStarted: of those, the ones the hero itself
//   was among the first attackers. A fight whose first bucket has hits both ways counts for neither (a clash).
// Null without the hits in the document (unknown, not "never started one").
public sealed record FightInitiationMetrics(int Fights, int? TeamStarted, int? HeroStarted);

// Domain service: the FightInitiationMetrics of one hero.
public static class FightInitiationMetricsCalculator
{
    public const int LeadInSeconds = 15;

    // fightsIn: the team fights the hero was in; teamOf: every hero's team.
    public static FightInitiationMetrics Calculate(
        int heroId, Team team, IReadOnlyList<Teamfight> fightsIn, IReadOnlyList<HeroHit>? hits, IReadOnlyDictionary<int, Team> teamOf)
    {
        if (hits is null)
        {
            return new FightInitiationMetrics(fightsIn.Count, null, null);
        }

        int teamStarted = 0, heroStarted = 0;
        foreach (var fight in fightsIn)
        {
            var between = hits.Where(hit => hit.Seconds >= fight.StartSeconds - LeadInSeconds && hit.Seconds <= fight.EndSeconds
                    && teamOf.TryGetValue(hit.AttackerHeroId, out var attacker) && teamOf.TryGetValue(hit.TargetHeroId, out var target)
                    && attacker != target)
                .ToList();
            if (between.Count == 0)
            {
                continue;
            }

            var firstSecond = between.Min(hit => hit.Seconds);
            var first = between.Where(hit => hit.Seconds == firstSecond).ToList();
            if (first.All(hit => teamOf[hit.AttackerHeroId] == team))
            {
                teamStarted++;
                if (first.Any(hit => hit.AttackerHeroId == heroId)) heroStarted++;
            }
        }

        return new FightInitiationMetrics(fightsIn.Count, teamStarted, heroStarted);
    }
}
