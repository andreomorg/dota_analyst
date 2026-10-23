using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>Values for each region, split into early game (0-10 minutes), mid game (10-20 minutes), and late game (20+ minutes).</summary>
public record RegionPhaseValues(RegionValues EarlyGame, RegionValues MidGame, RegionValues LateGame);
