using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// The Smokes of Deceit a hero used and how many of them were followed by an enemy hero dying within KillWindowSeconds
// (the gank that came of it). Null without the smokes in the document.
public sealed record SmokeMetrics(int Smokes, int SmokesWithKill);

// Domain service: the SmokeMetrics of one hero.
public static class SmokeMetricsCalculator
{
    public const int KillWindowSeconds = 60;

    public static SmokeMetrics? Calculate(int heroId, Team team, ReplayActivity? activity, IReadOnlyList<HeroDeath> deaths,
        IReadOnlyDictionary<int, Team> teamOf)
    {
        if (activity is null)
        {
            return null;
        }

        var smokes = activity.Smokes.Where(smoke => smoke.HeroId == heroId && smoke.Seconds >= 0).ToList();
        var enemyDeaths = deaths.Where(death => teamOf.GetValueOrDefault(death.HeroId) != team).Select(death => death.Seconds).ToList();
        return new SmokeMetrics(smokes.Count,
            smokes.Count(smoke => enemyDeaths.Any(second => second > smoke.Seconds && second <= smoke.Seconds + KillWindowSeconds)));
    }
}
