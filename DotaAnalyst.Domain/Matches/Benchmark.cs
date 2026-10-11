namespace DotaAnalyst.Domain.Matches;

// Value object: how a player's metric compares to other players of the same hero.
// Percentile goes from 0 to 1 and higher is always better (even for deaths).
public record Benchmark
{
    public Benchmark(BenchmarkMetric metric, double value, double percentile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percentile, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 1);

        Metric = metric;
        Value = value;
        Percentile = percentile;
    }

    public BenchmarkMetric Metric { get; }

    public double Value { get; }

    public double Percentile { get; }
}
