using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Tests.Domain;

public class MapPathfinderTests
{
    internal static GameMap Map(bool wall = false, bool trees = true)
    {
        var nav = Enumerable.Repeat((byte)1, 32 * 24).ToArray();
        if (wall) for (var y = 4; y < 17; y++) nav[y * 32 + 12] = 0;
        return new("test", new MapGrid(64, 32, 24, -512, -512, nav, new short[nav.Length]),
            trees ? [new MapPoint(256, 0)] : [], [], [], [], [], [], [], []);
    }

    [Fact]
    public void StandingTreeDetours_CutTreeAllowsStraightRoute()
    {
        var map = Map();
        var standing = MapPathfinder.Find(map, new(0, 0), new(768, 0), new HashSet<int>())!;
        Assert.Contains(standing, p => p.Y != 0);
        var cut = MapPathfinder.Find(map, new(0, 0), new(768, 0), new HashSet<int> { 0 })!;
        Assert.All(cut, p => Assert.Equal(0, p.Y));
        Assert.Equal(768, Length(cut));
        Assert.True(Length(standing) > Length(cut));
    }

    [Fact]
    public void WallBetweenNodesCannotBeSkipped()
    {
        var path = MapPathfinder.Find(Map(wall: true), new(0, 0), new(768, 0), new HashSet<int> { 0 })!;
        Assert.NotNull(path);
        Assert.Contains(path, p => p.Y >= 576 || p.Y < -256);
        Assert.True(Length(path) > 768);
    }

    [Fact]
    public void MissingInputsAndBudgetAreUnknown()
    {
        Assert.Null(MapPathfinder.Find(null, new(0, 0), new(768, 0), new HashSet<int>()));
        Assert.Null(MapPathfinder.Find(Map(), new(0, 0), new(768, 0), null));
        Assert.Null(MapPathfinder.Find(Map(trees: false), new(0, 0), new(768, 0), new HashSet<int>()));
        Assert.Null(MapPathfinder.Find(Map(), new(0, 0), new(768, 0), new HashSet<int> { 0 }, 1));
        Assert.Null(MapPathfinder.Find(Map(), new(-9999, 0), new(768, 0), new HashSet<int>()));
    }

    [Fact]
    public void DiagonalCostAndCoincidentEndpoints()
    {
        var path = MapPathfinder.Find(Map(), new(0, 0), new(256, 256), new HashSet<int> { 0 })!;
        Assert.Equal(256 * Math.Sqrt(2), Length(path), 6);
        Assert.Single(MapPathfinder.Find(Map(), new(0, 0), new(0, 0), new HashSet<int> { 0 })!);
    }

    private static double Length(IReadOnlyList<MapPoint> path) => path.Zip(path.Skip(1), (a, b) => a.DistanceTo(b)).Sum();
}
