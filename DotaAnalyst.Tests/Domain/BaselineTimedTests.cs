using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Tests.Domain;

public class BaselineTimedTests
{
    // A match of `minutes` minutes: a point per minute and the end, last hits = 5 per minute x `pace`, deaths = 1 per
    // 10 minutes x `pace`.
    private static HeroStatHistory Match(int minutes, int pace)
    {
        var seconds = Enumerable.Range(0, minutes + 1).Select(minute => minute * 60).Append(minutes * 60 + 30).ToList();
        IReadOnlyList<int> Rising(int perMinute) => seconds.Select(second => second / 60 * perMinute).ToList();
        return new HeroStatHistory(seconds, new Dictionary<StatKey, IReadOnlyList<int>>
        {
            [StatKey.LastHits] = Rising(5 * pace),
            [StatKey.Deaths] = seconds.Select(second => second / 600 * pace).ToList(),
            [StatKey.DamageTaken] = Rising(100),
        });
    }

    [Fact]
    public void DistributeTimed_UsesOnlyTheMatchesThatLastedUntilThen()
    {
        // Ten matches of 22 minutes and five of 45: at 20:00 fifteen, at 30:00 only five (too few).
        var histories = Enumerable.Range(1, 10).Select(pace => Match(22, pace))
            .Concat(Enumerable.Range(1, 5).Select(pace => Match(45, pace)))
            .ToList();

        var distributions = BaselineComparer.DistributeTimed(histories);

        Assert.Equal(15, distributions.Single(d => d is { Stat: StatKey.LastHits, Second: 1200 }).SampleSize);
        Assert.DoesNotContain(distributions, d => d.Second == 1800);
        // Damage taken is neither better high nor low: nothing to compare.
        Assert.DoesNotContain(distributions, d => d.Stat == StatKey.DamageTaken);
        Assert.Equal(BaselineComparer.QuantileCount, distributions[0].Quantiles.Count);
    }

    [Fact]
    public void CompareTimed_HigherOrLowerIsBetter_AsTheCatalogSays()
    {
        var distributions = BaselineComparer.DistributeTimed(Enumerable.Range(1, 11).Select(pace => Match(22, pace)).ToList());

        // The best pace of the base: the most last hits (better) and the most deaths (worse).
        var comparisons = BaselineComparer.CompareTimed(Match(22, 11), distributions);

        var lastHits = comparisons.Single(c => c is { Stat: StatKey.LastHits, Second: 600 });
        Assert.Equal((550, 300), (lastHits.Value, lastHits.Median));
        Assert.Equal(1, lastHits.BetterThan);
        Assert.Equal(0, comparisons.Single(c => c is { Stat: StatKey.Deaths, Second: 1200 }).BetterThan);
    }

    [Fact]
    public void CompareTimed_SkipsMomentsThePlayersMatchDidNotReach()
    {
        var distributions = BaselineComparer.DistributeTimed(Enumerable.Range(1, 10).Select(pace => Match(45, pace)).ToList());

        var comparisons = BaselineComparer.CompareTimed(Match(22, 3), distributions);

        Assert.Contains(comparisons, c => c.Second == 1200);
        Assert.DoesNotContain(comparisons, c => c.Second >= 1500);
    }

    [Fact]
    public void History_ValueAt_IsNullAfterTheEnd() =>
        Assert.Null(Match(22, 1).ValueAt(StatKey.LastHits, 1500));
}
