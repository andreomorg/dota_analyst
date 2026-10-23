using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class GameMapTests
{
    private static readonly int[] NearbyTreeIds = [1, 2];
    private static readonly int[] RemainingTreeIds = [2];

    // A 4 x 2 grid of 100-unit cells from (0, 0): bottom row walkable river and low ground, then a cliff and outside;
    // top row high ground, fountain, no floor, Tormentor area.
    private static GameMap Map(IReadOnlyList<MapPoint>? trees = null, IReadOnlyList<MapArea>? pits = null,
        IReadOnlyList<CreepPath>? paths = null, IReadOnlyList<MapLandmark>? landmarks = null) =>
        new("7.41",
            new MapGrid(100, 4, 2, 0, 0, [0x01, 0x01, 0x10, 0x14, 0x01, 0x11, 0x14, 0x01], [0, 128, 140, 0, 256, 384, MapGrid.NoFloor, -256]),
            trees ?? [], [], [], pits ?? [], [], [], paths ?? [], landmarks ?? []);

    [Theory]
    [InlineData(50, 50, 0)]
    [InlineData(150, 50, 1)]
    [InlineData(250, 50, 1)]     // 140 is still low ground
    [InlineData(50, 150, 2)]
    [InlineData(150, 150, 3)]
    [InlineData(350, 150, -2)]
    public void LevelAt_RoundsTheFloorToSteps(int x, int y, int expected)
    {
        Assert.Equal(expected, Map().LevelAt(x, y));
    }

    [Theory]
    [InlineData(250, 150)]       // no floor
    [InlineData(-10, 50)]        // outside the grid
    [InlineData(50, 200)]        // the top edge is outside
    public void LevelAt_NullWithoutFloor(int x, int y)
    {
        Assert.Null(Map().LevelAt(x, y));
    }

    [Theory]
    [InlineData(50, 50, true)]
    [InlineData(150, 150, true)]  // 0x11: fountain or pit
    [InlineData(250, 50, false)]  // 0x10: cliff
    [InlineData(350, 50, false)]  // 0x14: outside
    [InlineData(-50, 50, false)]
    public void IsWalkable_ByTheLowBit(int x, int y, bool expected)
    {
        Assert.Equal(expected, Map().IsWalkable(x, y));
    }

    [Fact]
    public void Trees_StandingOnesOnly()
    {
        var map = Map(trees: [new MapPoint(100, 100), new MapPoint(1000, 1000), new MapPoint(1100, 1000)]);

        Assert.Equal(NearbyTreeIds, map.TreesWithin(1050, 1000, 100).Order());
        Assert.Equal(RemainingTreeIds, map.TreesWithin(1050, 1000, 100, new HashSet<int> { 1 }));
        Assert.True(map.IsTreeAt(160, 40));
        Assert.False(map.IsTreeAt(170, 100));
        Assert.False(map.IsTreeAt(100, 100, new HashSet<int> { 0 }));
    }

    [Fact]
    public void Area_TurnedByItsYaw()
    {
        // A box 0..100 x 0..20 in its own space, turned 90 degrees: in the world it goes up from the origin.
        var area = new MapArea("pit", 500, 500, 90, 0, 0, 100, 20);

        Assert.True(area.Contains(490, 590));
        Assert.False(area.Contains(590, 510));
        Assert.Equal(490, area.Center.X, 6);
        Assert.Equal(550, area.Center.Y, 6);
    }

    [Fact]
    public void Pits_TheOneAPointIsInAndTheNearest()
    {
        var top = new MapArea("top", -3000, 2700, 0, -300, -300, 300, 300);
        var bottom = new MapArea("bottom", 3000, -2700, 180, -300, -300, 300, 300);
        var map = Map(pits: [top, bottom]);

        Assert.Same(top, map.PitAt(-3100, 2600));
        Assert.Null(map.PitAt(0, 0));
        Assert.Same(bottom, map.PitNearest(2000, -2000));
    }

    [Fact]
    public void CreepPaths_NearestOneAndItsDistance()
    {
        var middle = new CreepPath(Lane.Middle, Team.Radiant, [new MapPoint(-1000, -1000), new MapPoint(1000, 1000)]);
        var top = new CreepPath(Lane.Top, Team.Dire, [new MapPoint(-6000, 6000), new MapPoint(-6000, -3000)]);
        var map = Map(paths: [middle, top]);

        var nearest = map.NearestCreepPath(0, 200)!.Value;
        Assert.Same(middle, nearest.Path);
        Assert.Equal(Math.Sqrt(2) * 100, nearest.Distance, 6);
        Assert.Same(top, map.NearestCreepPath(0, 200, Team.Dire)!.Value.Path);
        Assert.Equal(Math.Sqrt(2) * 1000, middle.DistanceTo(2000, 2000), 6);   // past the end: the end itself
    }

    [Fact]
    public void Landmarks_NearestOfAKindAndTeam()
    {
        var map = Map(landmarks:
        [
            new MapLandmark(LandmarkKind.Tower, "radiant", Team.Radiant, 0, 0),
            new MapLandmark(LandmarkKind.Tower, "dire", Team.Dire, 1000, 0),
            new MapLandmark(LandmarkKind.LotusPool, "", null, 900, 0),
        ]);

        Assert.Equal("dire", map.NearestLandmark(LandmarkKind.Tower, 800, 0)!.Value.Landmark.Name);
        Assert.Equal("radiant", map.NearestLandmark(LandmarkKind.Tower, 800, 0, Team.Radiant)!.Value.Landmark.Name);
        Assert.Null(map.NearestLandmark(LandmarkKind.Outpost, 0, 0));
    }

    [Fact]
    public void Grid_NeedsOneValuePerCell()
    {
        Assert.Throws<ArgumentException>(() => new MapGrid(64, 2, 2, 0, 0, [1, 1, 1], [0, 0, 0, 0]));
    }
}
