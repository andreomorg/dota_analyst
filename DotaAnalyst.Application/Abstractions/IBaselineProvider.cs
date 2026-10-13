using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Abstractions;

// The player's metrics against the comparison base (same hero and role), and — with the player's statistics minute by
// minute (history, null for analyses without them) — the statistics of the catalog at each checkpoint. The base is
// published by the collector's machine (DistributionBaselineProvider reads it). An extra: null when it can't be read.
public interface IBaselineProvider
{
    Task<IReadOnlyList<BaselineComparisonSummary>?> CompareAsync(
        HeroMetrics player, int matchSeconds, HeroStatHistory? history, CancellationToken cancellationToken);

    Task<IReadOnlyList<BaselineComparisonSummary>?> CompareLaneMatchupAsync(
        HeroMetrics player, int opponentHeroId, int matchSeconds, HeroStatHistory? history, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BaselineComparisonSummary>?>([]);
}
