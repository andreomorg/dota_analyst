using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Analysis;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Baseline;

// The comparison from the finished base in the site's own database (published by the collector's machine: local
// database there, the Neon mirror in production).
public class DistributionBaselineProvider(
    IBaselineDistributionStore store,
    ILogger<DistributionBaselineProvider> logger) : IBaselineProvider
{
    private static readonly BaselineSource[] Sources = [BaselineSource.Pro, BaselineSource.HighRank];

    public async Task<IReadOnlyList<BaselineComparisonSummary>?> CompareAsync(
        HeroMetrics player, int matchSeconds, CancellationToken cancellationToken)
    {
        var baselines = new List<BaselineComparisonSummary>();
        try
        {
            foreach (var source in Sources)
            {
                var set = await store.FindAsync(player.HeroId, player.Role, source, cancellationToken);
                baselines.Add(new BaselineComparisonSummary(
                    source,
                    set?.Matches ?? 0,
                    BaselineComparer.Compare(player, matchSeconds, set?.Distributions ?? [])
                        .Select(comparison => new MetricComparisonSummary(
                            comparison.Metric, comparison.Value, comparison.Median, comparison.BetterThan, comparison.SampleSize))
                        .ToList()));
            }
        }
        catch (StorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not read the comparison base");
            return null;
        }

        return baselines;
    }
}
