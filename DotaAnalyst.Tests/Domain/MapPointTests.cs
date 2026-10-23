using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Tests.Domain;

public class MapPointTests
{
    [Fact]
    public void Distance_IsTheStraightLine()
    {
        var from = new MapPoint(0, 0);
        var to = new MapPoint(300, 400);

        Assert.Equal(500, from.DistanceTo(to));
        Assert.Equal(250_000, from.DistanceSquaredTo(to));
        Assert.Equal(from.DistanceTo(to), to.DistanceTo(from));
    }

    [Theory]
    [InlineData(500, true)] // the edge counts as within
    [InlineData(499.9, false)]
    public void IsWithin_IncludesTheEdge(double radius, bool within) =>
        Assert.Equal(within, new MapPoint(-150, 200).IsWithin(new MapPoint(150, 600), radius));
}
