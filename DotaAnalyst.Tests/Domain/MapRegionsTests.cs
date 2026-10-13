using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Domain;

// Points read off the game's minimap (pixel p of the 1024 px image is map unit p * 18.5 - 9472; y flipped).
public class MapRegionsTests
{
    [Theory]
    [InlineData(-7000, -7000, MapRegion.OwnBase)]      // Radiant fountain
    [InlineData(7000, 7000, MapRegion.EnemyBase)]      // Dire fountain
    [InlineData(-6450, 0, MapRegion.TopLane)]          // Radiant top towers' line
    [InlineData(0, 5950, MapRegion.TopLane)]           // Dire top towers' line
    [InlineData(0, 0, MapRegion.MiddleLane)]           // the middle of the map
    [InlineData(0, -6250, MapRegion.BottomLane)]
    [InlineData(6350, 0, MapRegion.BottomLane)]
    [InlineData(-3500, 3000, MapRegion.River)]         // the river by the top Roshan pit (px ~ 300, 340)
    [InlineData(-3500, 0, MapRegion.OwnJungle)]        // Radiant's jungle between the top and the middle lane (px 323, 512)
    [InlineData(3500, 0, MapRegion.EnemyJungle)]       // Dire's jungle (px 701, 512)
    [InlineData(0, 7800, MapRegion.River)]             // the water between the top lane and the map edge
    public void RegionOf_FromRadiant(int x, int y, MapRegion expected)
    {
        Assert.Equal(expected, MapRegions.RegionOf(Team.Radiant, x, y));
    }

    [Fact]
    public void BasesAndJungles_AreSeenFromTheTeam()
    {
        Assert.Equal(MapRegion.EnemyBase, MapRegions.RegionOf(Team.Dire, -7000, -7000));
        Assert.Equal(MapRegion.OwnJungle, MapRegions.RegionOf(Team.Dire, 3500, 0));
        Assert.Equal(MapRegion.EnemyJungle, MapRegions.RegionOf(Team.Dire, -3500, 0));
        Assert.Equal(MapRegion.MiddleLane, MapRegions.RegionOf(Team.Dire, 0, 0));
    }

    [Fact]
    public void OutsideTheMask_IsNotWater()
    {
        // Far off the map: no crash, jungle by side.
        Assert.Equal(MapRegion.OwnJungle, MapRegions.RegionOf(Team.Radiant, -20000, 3000));
    }
}
