using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Analysis;

namespace DotaAnalyst.Application.Abstractions;

// The player's metrics against the comparison base (same hero and role). The base lives on the project owner's
// machine (the collector's local database): there the comparison reads it directly (DatabaseBaselineProvider); in
// production it is asked through the tunnel (Infrastructure). An extra: null when it can't be reached.
public interface IBaselineProvider
{
    Task<IReadOnlyList<BaselineComparisonSummary>?> CompareAsync(
        HeroMetrics player, int matchSeconds, CancellationToken cancellationToken);
}
