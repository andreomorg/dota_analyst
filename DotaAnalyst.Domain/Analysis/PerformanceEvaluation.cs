using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Analysis;

public record MetricEvaluation(BenchmarkMetric Metric, double Value, double Percentile, PerformanceRating Rating);

public record PerformanceEvaluation(IReadOnlyList<MetricEvaluation> Metrics)
{
    public static readonly PerformanceEvaluation Empty = new([]);

    // Best first.
    public IReadOnlyList<MetricEvaluation> Strengths =>
        Metrics.Where(m => m.Rating == PerformanceRating.Strong).OrderByDescending(m => m.Percentile).ToList();

    // Worst first.
    public IReadOnlyList<MetricEvaluation> Weaknesses =>
        Metrics.Where(m => m.Rating == PerformanceRating.Weak).OrderBy(m => m.Percentile).ToList();
}
