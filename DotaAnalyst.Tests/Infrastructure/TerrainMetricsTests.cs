using System.Text.Json;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Infrastructure.GameData;
using DotaAnalyst.Infrastructure.Persistence.Replays;

namespace DotaAnalyst.Tests.Infrastructure;

// TerrainMetrics (MP8) on the real 7.41 map, and their place in the stored metrics.
public class TerrainMetricsTests
{
    private static readonly global::DotaAnalyst.Domain.Maps.GameMap Map = EmbeddedGameMaps.Map ?? throw new InvalidOperationException("No 7.41 map.");

    [Fact]
    public void HighGround_IsOwnOrEnemyBySide()
    {
        // Two samples in the Radiant base, one in the river.
        IReadOnlyList<PositionSample> samples = [new(10, -5800, -5600), new(12, -5800, -5600), new(14, -1640, 1112)];

        var radiant = TerrainMetricsCalculator.Calculate(Map, Team.Radiant, samples, []);
        var dire = TerrainMetricsCalculator.Calculate(Map, Team.Dire, samples, []);

        Assert.Equal(2.0 / 3, radiant.OwnHighGroundShare!.Value, 6);
        Assert.Equal(0.0, radiant.EnemyHighGroundShare);
        Assert.Equal(2.0 / 3, dire.EnemyHighGroundShare!.Value, 6);
        Assert.Equal(1.0 / 3, radiant.RiverLevelShare!.Value, 6);
        // Without the trees of the match, what depends on them is unknown.
        Assert.Null(radiant.NarrowGroundShare);
        Assert.Null(radiant.DeathsInDeepEnemyJungle);
    }

    [Fact]
    public void NoSamples_SharesUnknown_DeathsStillCounted()
    {
        IReadOnlyList<HeroDeath> deaths = [new(1, 900, 5600, 5200, KillerKind.Other, null, false)];

        var terrain = TerrainMetricsCalculator.Calculate(Map, Team.Radiant, [], deaths, new TreeTimeline([], []));

        Assert.Null(terrain.OwnHighGroundShare);
        Assert.Null(terrain.NarrowGroundShare);
        Assert.Equal(1, terrain.DeathsOnEnemyHighGround);
        Assert.NotNull(terrain.DeathsInNarrowGround);
    }

    [Fact]
    public void TreesCutDown_AreNotStandingForTheGroundAround()
    {
        // A wooded spot of the Radiant jungle; all of its trees down from 100 on.
        var (x, y) = Enumerable.Range(0, 40).SelectMany(i => Enumerable.Range(0, 30).Select(j => (X: -5000 + 100 * i, Y: -3000 + 100 * j)))
            .First(point => Map.IsWalkable(point.X, point.Y) && global::DotaAnalyst.Domain.Maps.TerrainZones.Classify(Map, point.X, point.Y)
                is { Area: global::DotaAnalyst.Domain.Maps.TerrainArea.Jungle, Side: Team.Radiant, TreesNearby: >= 4 });
        var cut = Map.TreesWithin(x, y, 600).Select(index => new TreeChange(100, index, Alive: false)).ToList();
        IReadOnlyList<PositionSample> before = [new(50, x, y)], after = [new(150, x, y)];

        var standing = TerrainMetricsCalculator.Calculate(Map, Team.Dire, before, [], new TreeTimeline([], cut));
        var down = TerrainMetricsCalculator.Calculate(Map, Team.Dire, after, [], new TreeTimeline([], cut));

        Assert.Equal(1.0, standing.DeepEnemyJungleShare);
        Assert.Equal(0.0, down.DeepEnemyJungleShare);
    }

    [Fact]
    public void Deaths_OnEnemyHighGround_AndBeforeTheHornLeftOut()
    {
        IReadOnlyList<HeroDeath> deaths = [new(1, 900, 5600, 5200, KillerKind.Other, null, false)];   // in the Dire base
        IReadOnlyList<PositionSample> samples = [new(-30, -5800, -5600), new(10, 0, 0)];

        var terrain = TerrainMetricsCalculator.Calculate(Map, Team.Radiant, samples, deaths);

        Assert.Equal(1, terrain.DeathsOnEnemyHighGround);
        Assert.Equal(0.0, terrain.OwnHighGroundShare);   // the pre-game sample does not count
    }

    [Fact]
    public void ObserverSeenShare_TreesCutAroundAWardOpenItsView()
    {
        // A spot in the Radiant jungle with trees around (the middle of the triangle between the lanes).
        var (x, y) = Enumerable.Range(0, 40).Select(i => (X: -4000 + 50 * i, Y: -1200))
            .First(point => Map.TreesWithin(point.X, point.Y, 400).Any() && Map.IsWalkable(point.X, point.Y));
        var allTrees = Enumerable.Range(0, Map.Trees.Count).ToHashSet();

        var standing = VisionMetricsCalculator.SeenShare(Map, x, y, new HashSet<int>())!.Value;
        var cut = VisionMetricsCalculator.SeenShare(Map, x, y, allTrees)!.Value;

        Assert.InRange(standing, 0.01, 1);
        Assert.True(cut > standing, $"cut {cut:P0} should see more than standing {standing:P0}");
    }

    [Fact]
    public void ShrineSide_OneShrineOnEachSide_AndFarFromThemUnknown()
    {
        var shrines = Map.Landmarks.Where(landmark => landmark.Kind == global::DotaAnalyst.Domain.Maps.LandmarkKind.WisdomShrine).ToList();

        Assert.Equal(2, shrines.Count);
        var sides = shrines.Select(shrine => MapObjectiveMetricsCalculator.ShrineSideAt(Map, shrine.X + 300, shrine.Y)).ToList();
        Assert.Contains(Team.Radiant, sides.Cast<Team>());
        Assert.Contains(Team.Dire, sides.Cast<Team>());
        Assert.Null(MapObjectiveMetricsCalculator.ShrineSideAt(Map, 0, 0));
    }

    [Fact]
    public void ForMatch_OnlyThePatchOfTheMap()
    {
        Assert.NotNull(EmbeddedGameMaps.ForMatch(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero)));
        Assert.Null(EmbeddedGameMaps.ForMatch(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));   // 7.40c
        Assert.Null(EmbeddedGameMaps.ForMatch(null));
    }

    [Fact]
    public void StoredMetrics_KeepTheTerrain_AndOlderRowsReadWithout()
    {
        var metrics = new HeroMetrics(1, Team.Radiant, 1, 3, 1, 1, 0.2, 0.1, 4000, 9000, 50, 0.3, 0, 0, null, 0, 0,
            Terrain: new TerrainMetrics(0.2, 0.05, 0.1, 0.08, 0.02, 1, 0, 2, 0),
            Vision: new VisionMetrics(0.35, 1, 1, 0, 2, 0.62),
            Objectives: new MapObjectiveMetrics(new MapObjectiveCounts(4, 2, 1, 3, 6, 5, 1, 0, 2), 2, 1, 0, null, 4, 3, 1, 2));

        var json = JsonSerializer.Serialize(StoredReplayMappings.ToStored(metrics), StoredReplayMappings.JsonOptions);
        var back = StoredReplayMappings.ToDomain(JsonSerializer.Deserialize<StoredHeroMetrics>(json, StoredReplayMappings.JsonOptions)!);

        Assert.Equal(metrics.Terrain, back.Terrain);
        Assert.Equal(metrics.Vision, back.Vision);
        Assert.Equal(metrics.Objectives, back.Objectives);
        var older = JsonSerializer.Deserialize<StoredHeroMetrics>(
            json.Replace("\"terrain\"", "\"unknownOld\"").Replace("\"vision\"", "\"unknownOlder\""), StoredReplayMappings.JsonOptions)!;
        Assert.Null(StoredReplayMappings.ToDomain(older).Terrain);
        Assert.Null(StoredReplayMappings.ToDomain(older).Vision);
    }
}
