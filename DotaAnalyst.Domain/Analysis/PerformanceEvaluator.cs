using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Analysis;

// Domain service: decides what counts as a good or bad performance, whatever the data source.
public static class PerformanceEvaluator
{
    public const double StrongThreshold = 0.70;
    public const double WeakThreshold = 0.30;

    public static PerformanceEvaluation Evaluate(MatchPlayer player) =>
        new(player.Benchmarks
            .Where(IsApplicable)
            .Select(b => new MetricEvaluation(b.Metric, b.Value, b.Percentile, Rate(b.Percentile)))
            .ToList());

    public static PerformanceRating Rate(double percentile) => percentile switch
    {
        >= StrongThreshold => PerformanceRating.Strong,
        <= WeakThreshold => PerformanceRating.Weak,
        _ => PerformanceRating.Average,
    };

    // Healing only matters for heroes that heal: zero healing is "not applicable", not a weakness.
    private static bool IsApplicable(Benchmark benchmark) =>
        !(benchmark.Metric == BenchmarkMetric.HeroHealingPerMinute && benchmark.Value == 0);
}
