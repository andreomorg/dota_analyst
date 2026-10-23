using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// How a hero did by day and by night (the game's own cycle, ReplayActivity.DayNight, temporary nights included):
// kills (killing blow or assist) and deaths at night, next to the totals, and the part of the game (from 0:00) that was
// night. Some heroes are made for the night (Night Stalker, Luna...); everyone sees less at night.
public sealed record DayNightMetrics(double NightShare, int Kills, int KillsAtNight, int Deaths, int DeathsAtNight);

// Domain service: the DayNightMetrics of one hero.
public static class DayNightMetricsCalculator
{
    // Null without the day/night changes in the document.
    public static DayNightMetrics? Calculate(
        int heroId, Team team, IReadOnlyList<HeroDeath> deaths, IReadOnlyDictionary<int, Team> teamOf,
        IReadOnlyList<DayNightChange>? cycle, int matchSeconds)
    {
        if (cycle is null || cycle.Count == 0 || matchSeconds <= 0)
        {
            return null;
        }

        var ordered = cycle.OrderBy(change => change.Seconds).ToList();
        // Before the first change the game is in daylight (it starts at dawn).
        bool IsNight(int second) => ordered.LastOrDefault(change => change.Seconds <= second) is { IsDay: false };

        var nightSeconds = Enumerable.Range(0, matchSeconds).Count(IsNight);
        var kills = deaths.Where(death => teamOf.GetValueOrDefault(death.HeroId) != team
            && (death.KillerHeroId == heroId || death.AssistHeroIds.Contains(heroId))).ToList();
        var own = deaths.Where(death => death.HeroId == heroId).ToList();
        return new DayNightMetrics(
            Math.Round((double)nightSeconds / matchSeconds, 3),
            kills.Count, kills.Count(kill => IsNight(kill.Seconds)),
            own.Count, own.Count(death => IsNight(death.Seconds)));
    }
}
