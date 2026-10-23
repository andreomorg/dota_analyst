using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Matches;

internal static partial class MatchAnalysisMapper
{
    internal static MatchTimelineChart ToTimelineChart(
        ReplayAnalysis replay, MatchTimeline timeline, MatchPlayer? player, NameLookup names)
    {
        var perspective = player?.Team ?? Team.Radiant;
        var lead = TimelineEvaluator.LeadOf(timeline, perspective);

        // The lane chart shows the same heroes as the laning phase: the player, allies and opponents of their lane.
        var lane = new List<TimelineHero>();
        if (player is not null && LaneOutcomeEvaluator.Evaluate(replay, player.HeroId) is { } evaluation)
        {
            void Add(LaningStats stats, LaneSide side)
            {
                if (timeline.FindHero(stats.HeroId) is { } hero)
                {
                    lane.Add(new TimelineHero(hero.HeroId, names.Hero(hero.HeroId), side, hero.NetWorth, hero.Experience));
                }
            }

            Add(evaluation.Player, LaneSide.Player);
            evaluation.Allies.ToList().ForEach(ally => Add(ally, LaneSide.Ally));
            evaluation.Opponents.ToList().ForEach(opponent => Add(opponent, LaneSide.Opponent));
        }

        return new MatchTimelineChart(timeline.Seconds, perspective, lead.NetWorth, lead.Experience, lane);
    }
}
