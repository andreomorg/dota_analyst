using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Infrastructure.GameData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Tests.Infrastructure;

// The real 7.41 map embedded in the Infrastructure (tools/MapExtractor). Expected values were read off the file's grid
// at places whose position is known from replays (work/agents/map-survey.md).
public class GameMapFileTests
{
    private static readonly GameMap Map = new GameMapCatalog(Options.Create(new GameDataOptions()), NullLogger<GameMapCatalog>.Instance)
        .ForMatch(new GameDataOptions().Since) ?? throw new InvalidOperationException("The 7.41 map did not load.");

    [Theory]
    [InlineData(-1640, 1112, 0)]      // top power rune, in the river
    [InlineData(-3500, 3000, 0)]      // river by the top pit
    [InlineData(-3190, -2926, 1)]     // Radiant tier-2 middle tower
    [InlineData(-5800, -5600, 2)]     // Radiant base
    [InlineData(5600, 5200, 2)]       // Dire base
    [InlineData(-7680, 6336, -2)]     // Tormentor, below the river
    public void LevelAt_KnownPlaces(int x, int y, int expected)
    {
        Assert.Equal(expected, Map.LevelAt(x, y));
    }

    [Fact]
    public void Grid_WalkableAndOutside()
    {
        Assert.Equal("7.41", Map.Patch);
        Assert.True(Map.IsWalkable(-3190, -2926));
        Assert.True(Map.IsWalkable(-3194, 2395));     // Roshan in the top pit
        Assert.False(Map.IsWalkable(-9900, -10000));
        Assert.Null(Map.LevelAt(-9900, -10000));
    }

    [Fact]
    public void Pits_ContainRoshanOnBothSides()
    {
        // Roshan's unit in one match and the other pit's spawner: inside only with the pits turned by their yaw.
        var top = Map.PitAt(-3194, 2395);
        var bottom = Map.PitAt(2830, -2740);

        Assert.NotNull(top);
        Assert.NotNull(bottom);
        Assert.NotSame(top, bottom);
        Assert.Same(bottom, Map.PitNearest(2830, -2740));
        Assert.Null(Map.PitAt(0, 0));
    }

    [Fact]
    public void Trees_AndNoWardZones()
    {
        Assert.Equal(2475, Map.Trees.Count);
        var tree = Map.Trees.ToList().IndexOf(new MapPoint(-6080, -7168));
        Assert.True(tree >= 0);
        Assert.True(Map.IsTreeAt(-6080, -7168));
        Assert.DoesNotContain(tree, Map.TreesWithin(-6080, -7168, 10, new HashSet<int> { tree }));
        Assert.True(Map.InNoWardZone(-7000, -6500));   // around the Radiant fountain
        Assert.False(Map.InNoWardZone(0, 0));
    }

    [Fact]
    public void CreepPaths_OnePerLaneAndTeam()
    {
        Assert.Equal(6, Map.CreepPaths.Count);
        var radiantTop = Map.CreepPaths.Single(path => path.Lane == Lane.Top && path.Team == Team.Radiant);
        Assert.Equal(new MapPoint(-6584, -4224), radiantTop.Points[0]);
        Assert.Equal(new MapPoint(5323, 4852), radiantTop.Points[^1]);
        Assert.Equal(Lane.Middle, Map.NearestCreepPath(0, 0)!.Value.Path.Lane);
    }

    [Fact]
    public void Landmarks_ByKindAndTeam()
    {
        Assert.Equal(2, Map.Landmarks.Count(landmark => landmark.Kind == LandmarkKind.Ancient));
        Assert.Equal(11, Map.Landmarks.Count(landmark => landmark.Kind == LandmarkKind.Tower && landmark.Team == Team.Radiant));
        Assert.Equal(10, Map.Landmarks.Count(landmark => landmark.Kind == LandmarkKind.Lantern));
        Assert.Equal("dota_goodguys_tower1_top", Map.NearestLandmark(LandmarkKind.Tower, -6336, 1800, Team.Radiant)!.Value.Landmark.Name);
    }

    [Fact]
    public void Layers_AndSource_AreKept()
    {
        Assert.Equal(19, Map.FogRevealers.Count);
        Assert.Equal(5, Map.Currents.Count);
        Assert.Equal("dota.vpk", Map.Source!.File);
        Assert.Equal(64, Map.Source.Sha256.Length);
    }

    [Fact]
    public void Catalog_OnlyForMatchesOfThePatch()
    {
        var catalog = new GameMapCatalog(Options.Create(new GameDataOptions()), NullLogger<GameMapCatalog>.Instance);

        Assert.Null(catalog.ForMatch(null));
        Assert.Null(catalog.ForMatch(new GameDataOptions().Since.AddDays(-1)));
        Assert.Same(catalog.ForMatch(new GameDataOptions().Since), catalog.ForMatch(new GameDataOptions().Since.AddDays(30)));
    }

    [Fact]
    public void Read_RefusesAFileThatIsNotAMap()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        Assert.Throws<InvalidDataException>(() => GameMapFile.Read(stream));
    }
}
