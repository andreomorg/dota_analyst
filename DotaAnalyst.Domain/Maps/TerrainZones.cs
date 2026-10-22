using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Maps;

// The kind of place a point is, by the real terrain of the patch (MP5). The old MapRegions (rectangles, tower lines and
// the minimap's river) stay as they are until the switch is approved.
public enum TerrainArea
{
    Base,
    Lane,
    River,
    Jungle,
    RoshanPit,
    TormentorPit,
}

// Where a point is, by the map of its patch: the area, whose side of the map it is (the base's team; else by the
// river's diagonal), the lane (Lane areas only), the terrain level (0 river, 1 low ground, 2 high ground), the objective
// it is next to, how many standing trees are around it and how open it is (the share of the ground around it a unit can
// walk on, without trees: low in narrow passages and the deep jungle).
public sealed record TerrainZone(
    TerrainArea Area, Team Side, Lane? Lane, int? Level, LandmarkKind? NearObjective, int TreesNearby, double Openness)
{
    public bool HighGround => Level >= 2;
}

// Domain service: the terrain zone of a point. Checked by painting the zones over the map (tools/RegionPainter,
// work/map/zones-7.41.png).
public static class TerrainZones
{
    // A point this close to its lane's creep path is in the lane. Measured on two 7.41 replays (22/10/2026): the share
    // of laners' positions (1:00-10:00, outside the bases) within it grows smoothly — 57% at 500, 64% at 700, 68% at 800,
    // 73% at 1,000 (the old MapRegions) — with no natural width; 800 follows the corridor seen on the map without the
    // jungle edges the old 1,000 took in.
    public const int LaneHalfWidth = 800;

    // Objectives a point counts as next to, within ObjectiveRadius.
    public const int ObjectiveRadius = 600;

    private static readonly LandmarkKind[] Objectives =
    [
        LandmarkKind.LotusPool, LandmarkKind.WisdomShrine, LandmarkKind.Outpost, LandmarkKind.TwinGate, LandmarkKind.Shop,
        LandmarkKind.BountyRune, LandmarkKind.PowerRune, LandmarkKind.Lantern,
    ];

    // Trees and openness are counted within these radii.
    public const int TreeRadius = 300;
    public const int OpennessRadius = 256;

    public static TerrainZone Classify(GameMap map, double x, double y, IReadOnlySet<int>? downTrees = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var level = map.LevelAt(x, y);
        var (area, side, lane) = AreaOf(map, x, y, level);
        var objective = Objectives
            .Select(kind => map.NearestLandmark(kind, x, y))
            .Where(found => found is { Distance: <= ObjectiveRadius })
            .OrderBy(found => found!.Value.Distance)
            .Select(found => (LandmarkKind?)found!.Value.Landmark.Kind)
            .FirstOrDefault();
        return new TerrainZone(area, side, lane, level, objective, map.TreesWithin(x, y, TreeRadius, downTrees).Count,
            Openness(map, x, y, downTrees));
    }

    private static (TerrainArea Area, Team Side, Lane? Lane) AreaOf(GameMap map, double x, double y, int? level)
    {
        var diagonalSide = x + y < 0 ? Team.Radiant : Team.Dire;
        if (map.BaseAt(x, y) is { } baseTeam)
        {
            return (TerrainArea.Base, baseTeam, null);
        }

        if (map.PitAt(x, y) is not null)
        {
            return (TerrainArea.RoshanPit, diagonalSide, null);
        }

        if (level < 0)
        {
            return (TerrainArea.TormentorPit, diagonalSide, null);
        }

        if (map.NearestCreepPath(x, y) is { Distance: <= LaneHalfWidth } path)
        {
            return (TerrainArea.Lane, diagonalSide, path.Path.Lane);
        }

        return (level == 0 ? TerrainArea.River : TerrainArea.Jungle, diagonalSide, null);
    }

    // The share of grid cells within OpennessRadius a unit can stand on (walkable, no standing tree).
    private static double Openness(GameMap map, double x, double y, IReadOnlySet<int>? downTrees)
    {
        var center = map.CellOf(x, y);
        var reach = (int)Math.Ceiling((double)OpennessRadius / map.Grid.CellSize);
        int cells = 0, open = 0;
        for (var dc = -reach; dc <= reach; dc++)
        {
            for (var dr = -reach; dr <= reach; dr++)
            {
                var cell = (center.Column + dc, center.Row + dr);
                var (cx, cy) = map.CenterOf(cell);
                if ((cx - x) * (cx - x) + (cy - y) * (cy - y) > OpennessRadius * OpennessRadius)
                {
                    continue;
                }

                cells++;
                if (map.IsWalkable(cx, cy) && !map.HasStandingTree(cell, downTrees))
                {
                    open++;
                }
            }
        }

        return cells == 0 ? 0 : (double)open / cells;
    }
}
