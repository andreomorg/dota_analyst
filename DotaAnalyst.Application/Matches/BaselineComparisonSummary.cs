using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>The player against one comparison base (same hero and role).</summary>
/// <param name="Source">pro (league matches; roles inferred from the laning) or highRank (public ranked matches of the highest ranks).</param>
/// <param name="Matches">How many matches of the hero in that role the base has.</param>
/// <param name="Metrics">The metrics with enough matches to compare (at least 10); empty while the base is small.</param>
/// <param name="Timed">The statistics of the catalog (stats.definitions) at 5, 10, 15, 20, 25, 30 and 40 minutes, against the base's matches that lasted until then (at least 10); empty while the base is small or the replay was read before the statistics per minute.</param>
public record BaselineComparisonSummary(
    BaselineSource Source, int Matches, IReadOnlyList<MetricComparisonSummary> Metrics,
    IReadOnlyList<TimedComparisonSummary>? Timed = null);
