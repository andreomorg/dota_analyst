using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Domain;

public class PerRegionAveragesTests
{
    private static PerRegion<double> All(double value) => PerRegion<double>.Of(_ => value);

    [Fact]
    public void OfAll_AveragesEachRegion_AndIsUnknownWhenAnyHeroLacksThem()
    {
        Assert.Equal(All(0.5), PerRegionAverages.OfAll([All(0.25), All(0.75)]));
        Assert.Null(PerRegionAverages.OfAll([All(0.25), null]));
        Assert.Null(PerRegionAverages.OfAll([]));
    }

    [Fact]
    public void OfKnown_LeavesOutTheHeroesWithoutRegions()
    {
        Assert.Equal(All(0.2), PerRegionAverages.OfKnown([All(0.2), null]));
        Assert.Null(PerRegionAverages.OfKnown([null]));
    }

    [Fact]
    public void Phases_AreAveragedOneByOne()
    {
        var first = new PerGamePhase<PerRegion<double>>(All(0), All(1), All(2));
        var second = new PerGamePhase<PerRegion<double>>(All(2), All(3), All(4));

        Assert.Equal(new PerGamePhase<PerRegion<double>>(All(1), All(2), All(3)), PerRegionAverages.ByPhaseOfAll([first, second]));
        Assert.Null(PerRegionAverages.ByPhaseOfAll([first, null]));
    }
}
