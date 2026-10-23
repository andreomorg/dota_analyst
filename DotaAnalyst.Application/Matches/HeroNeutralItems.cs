using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>What one hero did with neutral items.</summary>
/// <param name="HeroId">Hero identifier.</param>
/// <param name="HeroName">Hero display name.</param>
/// <param name="IsCurrentPlayer">Whether this is the point of view's hero.</param>
/// <param name="Crafts">Crafts in time order.</param>
/// <param name="TiersSkipped">Tiers opened in time to craft that this hero never crafted.</param>
/// <param name="AverageDelaySeconds">Average delay after each tier opened; null without the build's tiers or crafts.</param>
/// <param name="MadstonesAcquired">Madstones acquired in the match; null without Madstone readings.</param>
/// <param name="MadstonesFound">Neutral item tokens found; null without Madstone readings.</param>
/// <param name="MadstonesUnused">Madstones still in hand at the end; null without Madstone readings.</param>
/// <param name="NeutralGold">Gold from neutral creeps; null without Madstone readings.</param>
public sealed record HeroNeutralItems(int HeroId, string HeroName, bool IsCurrentPlayer, IReadOnlyList<NeutralCraftInfo> Crafts,
    IReadOnlyList<int> TiersSkipped, double? AverageDelaySeconds, int? MadstonesAcquired, int? MadstonesFound, int? MadstonesUnused,
    int? NeutralGold);
