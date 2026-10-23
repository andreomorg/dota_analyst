using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>The player's numbers that show where to improve, compared within the match.</summary>
/// <param name="Role">The player's role-queue position, 1 (carry) to 5 (hard support).</param>
/// <param name="CounterpartHeroId">The enemy hero of the same role.</param>
/// <param name="CounterpartHeroName">Its name, or "Unknown hero".</param>
/// <param name="Player">The player's numbers.</param>
/// <param name="Counterpart">The numbers of the enemy of the same role.</param>
/// <param name="MatchAverage">The average of the ten heroes.</param>
/// <param name="Baselines">The player against other players of the same hero and role: pro matches and high-rank public matches of the last 60 days (one entry per source, even when empty); null when the comparison base could not be read.</param>
/// <param name="LaneMatchupHeroId">The opposing lane hero used for the lane matchup baseline, when it could be identified.</param>
/// <param name="LaneMatchupHeroName">Name of the opposing lane hero.</param>
/// <param name="LaneMatchupBaselines">The player against this hero's lane matchup in pro and high-rank matches; each metric needs at least 10 samples, null if the comparison base could not be read.</param>
/// <param name="Insights">What to improve (positioning, then economy, then vision); empty when nothing stands out.</param>
public record SmartStats(
    int Role,
    int CounterpartHeroId,
    string CounterpartHeroName,
    MetricsSummary Player,
    MetricsSummary Counterpart,
    MetricsSummary MatchAverage,
    IReadOnlyList<InsightSummary> Insights,
    IReadOnlyList<BaselineComparisonSummary>? Baselines = null,
    int? LaneMatchupHeroId = null,
    string? LaneMatchupHeroName = null,
    IReadOnlyList<BaselineComparisonSummary>? LaneMatchupBaselines = null);
