namespace DotaAnalyst.Domain.Analysis;

// Where the comparison base comes from.
public enum BaselineSource
{
    // Professional league matches (OpenDota's pro matches; roles inferred).
    Pro,
    // Public ranked matches of the highest ranks OpenDota lists.
    HighRank,
}

// The numbers compared with the base. Counts are per 30 minutes of game, so matches of different lengths compare.
public enum ComparedMetric
{
    DeathsPer30,
    IsolatedDeathsPer30,
    EnemyJungleDeathsPer30,
    AloneOnEnemySide,
    NetWorthAt10,
    LastHitsAt10,
    NetWorthAt20,
    ObserversPer30,
    SentriesPer30,
    ObserverLifetime,
    EnemyWardsDestroyedPer30,
}

// One hero's metrics in one match of the base, with the match's length (for the per-30-minutes numbers).
public record BaselineSample(HeroMetrics Metrics, int MatchSeconds);

// A metric's distribution in the base: the values at percentiles 0, 5, 10 ... 100 (QuantileCount points) and how
// many matches it came from. What is published to production: small however big the base is.
public record MetricDistribution(ComparedMetric Metric, IReadOnlyList<double> Quantiles, int SampleSize)
{
    public virtual bool Equals(MetricDistribution? other) =>
        other is not null && Metric == other.Metric && SampleSize == other.SampleSize && Quantiles.SequenceEqual(other.Quantiles);

    public override int GetHashCode() => HashCode.Combine(Metric, SampleSize);
}

// How the player did on one metric against the base: the median of the base and the share (0..1) of the base the
// player did better than.
public record MetricComparison(ComparedMetric Metric, double Value, double Median, double BetterThan, int SampleSize);

// Domain service: the base's distributions (made where the base is, on the project owner's machine) and the player's
// metrics against them (wherever the distributions are read: locally or in production).
public static class BaselineComparer
{
    // Fewer matches than this say too little: no distribution.
    public const int MinSampleSize = 10;

    // Percentiles 0, 5, 10 ... 100.
    public const int QuantileCount = 21;

    private const double ThirtyMinutes = 1800;

    // Each metric: how to read it (null when the hero doesn't have it) and whether higher is better.
    private static readonly (ComparedMetric Metric, Func<HeroMetrics, int, double?> Read, bool HigherIsBetter)[] Metrics =
    [
        (ComparedMetric.DeathsPer30, (hero, seconds) => Per30(hero.Deaths, seconds), false),
        (ComparedMetric.IsolatedDeathsPer30, (hero, seconds) => Per30(hero.IsolatedDeaths, seconds), false),
        (ComparedMetric.EnemyJungleDeathsPer30, (hero, seconds) => hero.DeathsByRegion is { } deaths ? Per30(deaths.EnemyJungle, seconds) : null, false),
        (ComparedMetric.AloneOnEnemySide, (hero, _) => hero.AloneOnEnemySideShare, false),
        (ComparedMetric.NetWorthAt10, (hero, _) => hero.NetWorthAt10, true),
        (ComparedMetric.LastHitsAt10, (hero, _) => hero.LastHitsAt10, true),
        (ComparedMetric.NetWorthAt20, (hero, _) => hero.NetWorthAt20, true),
        (ComparedMetric.ObserversPer30, (hero, seconds) => Per30(hero.ObserversPlaced, seconds), true),
        (ComparedMetric.SentriesPer30, (hero, seconds) => Per30(hero.SentriesPlaced, seconds), true),
        (ComparedMetric.ObserverLifetime, (hero, _) => hero.ObserverAverageLifetimeSeconds, true),
        (ComparedMetric.EnemyWardsDestroyedPer30, (hero, seconds) => Per30(hero.EnemyWardsDestroyed, seconds), true),
    ];

    // The distribution of each metric the base has at least MinSampleSize values of.
    public static IReadOnlyList<MetricDistribution> Distribute(IReadOnlyList<BaselineSample> samples)
    {
        var distributions = new List<MetricDistribution>();
        foreach (var (metric, read, _) in Metrics)
        {
            var values = samples.Select(sample => read(sample.Metrics, sample.MatchSeconds)).OfType<double>().Order().ToList();
            if (values.Count < MinSampleSize)
            {
                continue;
            }

            var quantiles = Enumerable.Range(0, QuantileCount)
                .Select(i => Percentile(values, (double)i / (QuantileCount - 1)))
                .ToList();
            distributions.Add(new MetricDistribution(metric, quantiles, values.Count));
        }

        return distributions;
    }

    // The player against each distribution the player has a value for.
    public static IReadOnlyList<MetricComparison> Compare(
        HeroMetrics player, int playerMatchSeconds, IReadOnlyList<MetricDistribution> distributions)
    {
        var comparisons = new List<MetricComparison>();
        foreach (var (metric, read, higherIsBetter) in Metrics)
        {
            if (read(player, playerMatchSeconds) is not { } value
                || distributions.FirstOrDefault(distribution => distribution.Metric == metric) is not { Quantiles.Count: QuantileCount } distribution)
            {
                continue;
            }

            var below = ShareBelow(distribution.Quantiles, value);
            comparisons.Add(new MetricComparison(
                metric, value, distribution.Quantiles[QuantileCount / 2], higherIsBetter ? below : 1 - below, distribution.SampleSize));
        }

        return comparisons;
    }

    // Where the value falls in the distribution (0..1), interpolating between the percentiles; on a run of equal
    // percentiles (many players with 0 deaths) the middle of the run, like a tie counting half.
    private static double ShareBelow(IReadOnlyList<double> quantiles, double value)
    {
        var last = quantiles.Count - 1;
        if (value < quantiles[0])
        {
            return 0;
        }

        if (value > quantiles[last])
        {
            return 1;
        }

        var first = Enumerable.Range(0, quantiles.Count).First(i => quantiles[i] >= value);
        if (quantiles[first] == value)
        {
            var end = Enumerable.Range(0, quantiles.Count).Last(i => quantiles[i] == value);
            return (first + end) / 2.0 / last;
        }

        var fraction = (value - quantiles[first - 1]) / (quantiles[first] - quantiles[first - 1]);
        return (first - 1 + fraction) / last;
    }

    // Linear interpolation between the sorted values (the usual "type 7" percentile).
    private static double Percentile(List<double> sorted, double p)
    {
        var position = p * (sorted.Count - 1);
        var low = (int)Math.Floor(position);
        var high = (int)Math.Ceiling(position);
        return sorted[low] + (sorted[high] - sorted[low]) * (position - low);
    }

    private static double? Per30(int count, int seconds) => seconds <= 0 ? null : count * ThirtyMinutes / seconds;
}
