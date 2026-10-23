using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>A neutral tier of the match's build.</summary>
/// <param name="Tier">Tier, 1 to 5.</param>
/// <param name="OpensAtSeconds">Game-clock second it opens.</param>
/// <param name="CraftCost">Madstones a craft costs.</param>
public sealed record NeutralTierInfo(int Tier, int OpensAtSeconds, int CraftCost);
