using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>The neutral items of the match (patch 7.38+: crafted from Madstones, one artifact and one enhancement per tier).</summary>
/// <param name="Tiers">When each tier opened (game clock) and its Madstone cost, from the game build of the match; empty when the build is not known here (no delays then).</param>
/// <param name="Heroes">Every hero, the point of view first.</param>
public sealed record MatchNeutrals(IReadOnlyList<NeutralTierInfo> Tiers, IReadOnlyList<HeroNeutralItems> Heroes);
