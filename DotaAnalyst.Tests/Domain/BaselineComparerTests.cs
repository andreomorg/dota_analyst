using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Domain;

public class BaselineComparerTests
{
    private static HeroMetrics Hero(int deaths, int netWorthAt10, int observers = 0, int? lifetime = null) =>
        new(1, Team.Radiant, 5, deaths, 0, 0, 0, 0, netWorthAt10, null, 10, 0.1, observers, 0, lifetime, 0, 0,
            PerRegion<double>.Of(_ => 0.125), PerRegion<int>.Of(_ => 0));

    // Ten 30-minute matches with 1..10 deaths and 1000..10000 net worth at 10:00.
    private static readonly IReadOnlyList<MetricDistribution> Base = BaselineComparer.Distribute(
        Enumerable.Range(1, 10).Select(i => new BaselineSample(Hero(i, i * 1000), 1800)).ToList());

    [Fact]
    public void Distribute_21Percentiles_PerMetricWithEnoughValues()
    {
        var deaths = Base.Single(distribution => distribution.Metric == ComparedMetric.DeathsPer30);

        Assert.Equal(BaselineComparer.QuantileCount, deaths.Quantiles.Count);
        Assert.Equal((1, 5.5, 10, 10), (deaths.Quantiles[0], deaths.Quantiles[10], deaths.Quantiles[20], deaths.SampleSize));
        // No observers in the base: no observer lifetime distribution.
        Assert.DoesNotContain(Base, distribution => distribution.Metric == ComparedMetric.ObserverLifetime);
        Assert.Empty(BaselineComparer.Distribute(Enumerable.Range(1, 9).Select(i => new BaselineSample(Hero(i, 0), 1800)).ToList()));
    }

    [Fact]
    public void LowerIsBetter_ForDeaths_AndCountsArePer30Minutes()
    {
        // 2 deaths in 15 minutes = 4 per 30 minutes: about two thirds of the base (1..10) died more.
        var deaths = BaselineComparer.Compare(Hero(2, 0), 900, Base).Single(c => c.Metric == ComparedMetric.DeathsPer30);

        Assert.Equal((4.0, 5.5, 10), (deaths.Value, deaths.Median, deaths.SampleSize));
        Assert.Equal(0.67, deaths.BetterThan, precision: 2);
    }

    [Fact]
    public void HigherIsBetter_ForNetWorth_AndTheEnds()
    {
        Assert.Equal(1, BaselineComparer.Compare(Hero(5, 20000), 1800, Base).Single(c => c.Metric == ComparedMetric.NetWorthAt10).BetterThan);
        Assert.Equal(0, BaselineComparer.Compare(Hero(5, 500), 1800, Base).Single(c => c.Metric == ComparedMetric.NetWorthAt10).BetterThan);
    }

    [Fact]
    public void LowerIsBetter_ForFirstBigItemTiming()
    {
        var samples = Enumerable.Range(1, 10)
            .Select(i => new BaselineSample(Hero(i, 0) with { FirstBigItemSeconds = i * 60 }, 1800))
            .ToList();
        var baseline = BaselineComparer.Distribute(samples);

        var comparison = Assert.Single(BaselineComparer.Compare(
            Hero(0, 0) with { FirstBigItemSeconds = 120 }, 1800, baseline),
            metric => metric.Metric == ComparedMetric.FirstBigItemSeconds);

        Assert.Equal(330, comparison.Median);
        Assert.True(comparison.BetterThan > 0.7);
    }

    [Fact]
    public void ARunOfEqualPercentiles_CountsAsATie()
    {
        // Most of the base never died: dying 0 times is better than about half of it, not all of it.
        var zeros = Enumerable.Range(0, 10).Select(i => new BaselineSample(Hero(i < 8 ? 0 : 5, 0), 1800)).ToList();

        var deaths = BaselineComparer.Compare(Hero(0, 0), 1800, BaselineComparer.Distribute(zeros))
            .Single(c => c.Metric == ComparedMetric.DeathsPer30);

        Assert.InRange(deaths.BetterThan, 0.55, 0.7);
    }
}
