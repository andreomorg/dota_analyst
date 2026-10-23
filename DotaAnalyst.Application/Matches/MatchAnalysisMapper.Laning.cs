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
    // Null when the player's hero is not in the replay.
    internal static LaningPhase? ToLaningPhase(ReplayAnalysis replay, MatchPlayer player, NameLookup names)
    {
        if (LaneOutcomeEvaluator.Evaluate(replay, player.HeroId) is not { } lane)
        {
            return null;
        }

        LaningPlayer ToLaningPlayer(LaningStats stats) => new(
            HeroId: stats.HeroId,
            HeroName: names.Hero(stats.HeroId),
            LastHits: stats.LastHits,
            Denies: stats.Denies,
            NetWorth: stats.NetWorth,
            Experience: stats.Experience,
            Deaths: stats.Deaths);

        return new LaningPhase(
            EndsAtSeconds: replay.EndsAtSeconds,
            Lane: lane.Player.Lane,
            Outcome: lane.Outcome,
            NetWorthDifference: lane.NetWorthDifference,
            Player: ToLaningPlayer(lane.Player),
            Allies: lane.Allies.Select(ToLaningPlayer).ToList(),
            Opponents: lane.Opponents.Select(ToLaningPlayer).ToList());
    }
}
