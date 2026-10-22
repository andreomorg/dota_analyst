using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Infrastructure.GameData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Tests.Infrastructure;

// TerrainZones on the real 7.41 map (places known from replays and the map file; painted in work/map/zones-7.41.png).
public class TerrainZonesTests
{
    private static readonly GameMap Map = new GameMapCatalog(Options.Create(new GameDataOptions()), NullLogger<GameMapCatalog>.Instance)
        .ForMatch(new GameDataOptions().Since) ?? throw new InvalidOperationException("The 7.41 map did not load.");

    [Theory]
    [InlineData(-5800, -5600, TerrainArea.Base, Team.Radiant)]
    [InlineData(5600, 5200, TerrainArea.Base, Team.Dire)]
    [InlineData(-3194, 2395, TerrainArea.RoshanPit, Team.Radiant)]   // Roshan in the top pit (x + y < 0)
    [InlineData(-7680, 6336, TerrainArea.TormentorPit, Team.Radiant)]
    [InlineData(-1640, 1112, TerrainArea.River, Team.Radiant)]       // top power rune
    [InlineData(-3500, 0, TerrainArea.Jungle, Team.Radiant)]
    [InlineData(3500, 0, TerrainArea.Jungle, Team.Dire)]
    public void Area_KnownPlaces(int x, int y, TerrainArea area, Team side)
    {
        var zone = TerrainZones.Classify(Map, x, y);

        Assert.Equal(area, zone.Area);
        Assert.Equal(side, zone.Side);
    }

    [Theory]
    [InlineData(0, 0, Lane.Middle)]
    [InlineData(-6450, 0, Lane.Top)]
    [InlineData(0, -6300, Lane.Bottom)]
    public void Lanes_ByTheCreepPaths(int x, int y, Lane lane)
    {
        var zone = TerrainZones.Classify(Map, x, y);

        Assert.Equal(TerrainArea.Lane, zone.Area);
        Assert.Equal(lane, zone.Lane);
    }

    [Fact]
    public void Attributes_HighGroundObjectivesTreesAndOpenness()
    {
        var jungle = TerrainZones.Classify(Map, -3500, 0);
        Assert.True(jungle.HighGround);
        Assert.Equal(LandmarkKind.LotusPool, TerrainZones.Classify(Map, -7548, 4209).NearObjective);
        Assert.Null(TerrainZones.Classify(Map, 0, 0).NearObjective);
        Assert.True(TerrainZones.Classify(Map, 0, 0).Openness > 0.8);

        // A tree's own spot: trees around, less open; cutting them all opens it again.
        var tree = Map.Trees[0];
        var wooded = TerrainZones.Classify(Map, tree.X, tree.Y);
        Assert.True(wooded.TreesNearby > 0);
        var cut = TerrainZones.Classify(Map, tree.X, tree.Y, Enumerable.Range(0, Map.Trees.Count).ToHashSet());
        Assert.Equal(0, cut.TreesNearby);
        Assert.True(cut.Openness >= wooded.Openness);
    }
}
