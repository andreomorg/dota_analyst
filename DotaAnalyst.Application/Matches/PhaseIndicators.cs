using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>Where and how a hero played, from its heatmap: the whole match and each phase.</summary>
/// <param name="All">The whole match; null when the hero has no samples.</param>
/// <param name="EarlyGame">0-10 minutes; null without samples there.</param>
/// <param name="MidGame">10-20 minutes; null without samples there.</param>
/// <param name="LateGame">From 20 minutes on; null without samples there.</param>
public record PhaseIndicators(
    PositionIndicatorsView? All, PositionIndicatorsView? EarlyGame, PositionIndicatorsView? MidGame, PositionIndicatorsView? LateGame);
