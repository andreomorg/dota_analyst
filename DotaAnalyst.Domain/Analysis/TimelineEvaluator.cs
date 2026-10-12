using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// A team's lead at each point of the timeline (negative when it was behind).
public record TeamLead(IReadOnlyList<int> NetWorth, IReadOnlyList<int> Experience);

// Domain service: reads leads out of the match timeline.
public static class TimelineEvaluator
{
    // The sum of the team's heroes minus the sum of the other team's, at each point.
    public static TeamLead LeadOf(MatchTimeline timeline, Team team)
    {
        IReadOnlyList<int> Lead(Func<HeroTimeline, IReadOnlyList<int>> values) =>
            Enumerable.Range(0, timeline.Seconds.Count)
                .Select(point => timeline.Heroes.Sum(hero => hero.Team == team ? values(hero)[point] : -values(hero)[point]))
                .ToList();

        return new TeamLead(Lead(hero => hero.NetWorth), Lead(hero => hero.Experience));
    }
}
