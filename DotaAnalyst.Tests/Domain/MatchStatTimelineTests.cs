using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Tests.Domain;

public class MatchStatTimelineTests
{
    [Fact]
    public void Catalog_HasEveryStatisticExactlyOnce()
    {
        Assert.Equal(Enum.GetValues<StatKey>().Order(), StatCatalog.All.Select(stat => stat.Key).Order());
        Assert.Contains(StatKey.LastHits, StatCatalog.SavedPerMinute);
        Assert.DoesNotContain(StatKey.ObserversPlaced, StatCatalog.SavedPerMinute);
        Assert.Equal(BetterWhen.Lower, StatCatalog.Get(StatKey.Deaths).Better);
    }

    // A 25:30 match: minutes 0, 10 and 20, then the end.
    private static readonly int[] Seconds = [0, 600, 1200, 1530];

    private static ReplayAnalysis Replay(IReadOnlyList<WardPlacement>? wards = null)
    {
        var lich = new LaningStats(31, Team.Radiant, Lane.Bottom, 20, 3, 2000, 2500, 1);
        var slardar = new LaningStats(28, Team.Dire, Lane.Bottom, 40, 1, 3000, 2800, 0);
        HeroTimeline Hero(LaningStats player, int[] lastHits) => new(
            player.HeroId, player.Team, [0, 1000, 2000, 3000], [0, 2000, 4000, 5000],
            new Dictionary<StatKey, IReadOnlyList<int>> { [StatKey.LastHits] = lastHits });
        return new ReplayAnalysis(
            ReplayBuilder.MatchIdOf(1), 600, [lich, slardar],
            new MatchTimeline(Seconds, [Hero(lich, [0, 20, 45, 60]), Hero(slardar, [0, 40, 90, 120])]),
            wards: wards);
    }

    [Theory]
    [InlineData(-30, 0)] // before 0:00: the first point
    [InlineData(600, 20)] // exactly on a point
    [InlineData(899, 20)] // between two points: the one before, nothing made up
    [InlineData(1530, 60)]
    [InlineData(5000, 60)] // after the end: the end
    public void ValueAt_IsTheLastPointAtOrBeforeTheMoment(int second, int expected)
    {
        var stats = MatchStatTimeline.From(Replay())!;

        Assert.Equal(expected, stats.ValueAt(31, StatKey.LastHits, second));
    }

    [Fact]
    public void Between_IsWhatHappenedInThePeriod()
    {
        var stats = MatchStatTimeline.From(Replay())!;

        Assert.Equal(25, stats.Between(31, StatKey.LastHits, 600, 1200));
        Assert.Equal(1000, stats.Between(28, StatKey.NetWorth, 600, 1200));
        Assert.Equal(1200, stats.SecondOfPointAt(1300));
    }

    [Fact]
    public void ValueAt_IsNull_ForAnUnknownHeroOrAStatisticTheHeroDoesNotHave()
    {
        var stats = MatchStatTimeline.From(Replay())!;

        Assert.Null(stats.ValueAt(99, StatKey.LastHits, 600));
        Assert.Null(stats.ValueAt(31, StatKey.Gold, 600));
        // Without wards in the analysis there is nothing to count them from.
        Assert.Null(stats.ValueAt(31, StatKey.ObserversPlaced, 600));
    }

    [Fact]
    public void Wards_AreCountedByEachPoint()
    {
        var stats = MatchStatTimeline.From(Replay(
        [
            new WardPlacement(WardKind.Observer, Team.Radiant, 31, 0, 0, -40, 300, destroyed: false, null),
            new WardPlacement(WardKind.Observer, Team.Radiant, 31, 0, 0, 700, 900, destroyed: true, 28),
            new WardPlacement(WardKind.Sentry, Team.Radiant, 31, 0, 0, 1300, null, destroyed: false, null),
            // An ally's ward destroyed by the Lich (a deny) is not an enemy ward.
            new WardPlacement(WardKind.Sentry, Team.Radiant, null, 0, 0, 100, 200, destroyed: true, 31),
        ]))!;

        Assert.Equal([1, 1, 2, 2], stats.FindHero(31)!.Series[StatKey.ObserversPlaced]);
        Assert.Equal([0, 0, 0, 1], stats.FindHero(31)!.Series[StatKey.SentriesPlaced]);
        Assert.Equal([0, 0, 1, 1], stats.FindHero(28)!.Series[StatKey.EnemyWardsDestroyed]);
        Assert.Equal([0, 0, 0, 0], stats.FindHero(31)!.Series[StatKey.EnemyWardsDestroyed]);
    }

    [Fact]
    public void From_IsNull_WithoutATimeline() =>
        Assert.Null(MatchStatTimeline.From(ReplayBuilder.AnalysisWithoutTimeline(1, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom))));

    [Fact]
    public void HeroTimeline_RejectsSeriesThatDoNotFitTheTimeline()
    {
        Assert.Throws<ArgumentException>(() => new HeroTimeline(1, Team.Radiant, [0, 1], [0, 1],
            new Dictionary<StatKey, IReadOnlyList<int>> { [StatKey.LastHits] = [0] }));
        Assert.Throws<ArgumentException>(() => new HeroTimeline(1, Team.Radiant, [0, 1], [0, 1],
            new Dictionary<StatKey, IReadOnlyList<int>> { [StatKey.LastHits] = [0, -1] }));
        // Counted from events, never saved.
        Assert.Throws<ArgumentException>(() => new HeroTimeline(1, Team.Radiant, [0, 1], [0, 1],
            new Dictionary<StatKey, IReadOnlyList<int>> { [StatKey.ObserversPlaced] = [0, 1] }));
    }

    [Fact]
    public void Analysis_WithoutEveryStatisticPerMinute_IsOutdated()
    {
        var current = ReplayBuilder.Analysis(1, ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom));
        var onlyNetWorthAndExperience = new ReplayAnalysis(
            current.MatchId, current.EndsAtSeconds, current.Players,
            new MatchTimeline([0, 600], [new HeroTimeline(31, Team.Radiant, [0, 1], [0, 1])]),
            [], [], [], [], [], [], [], []);

        Assert.False(current.IsOutdated);
        Assert.True(onlyNetWorthAndExperience.IsOutdated);
    }
}
