namespace DotaAnalyst.Domain.Matches;

// A part of the map, seen from one team (its own base and jungle, the enemy's).
public enum MapRegion
{
    OwnBase,
    EnemyBase,
    TopLane,
    MiddleLane,
    BottomLane,
    River,
    OwnJungle,
    EnemyJungle,
}

// Domain service: which region of the map a point (replay coordinates, Radiant bottom-left) is in.
// - Bases: behind the tier-3 towers, the same limits the Go parser uses (replay-parser/internal/laning/zone.go).
// - Lanes: a band of LaneHalfWidth around each lane's path, drawn through its towers (positions in zone.go). The Go
//   parser's lane zones are wider on purpose (they only pick each hero's lane in the first 10 minutes); here the
//   area between a side lane and the map edge is jungle, as it is in the current map.
// - River: the water of the game's own minimap (RiverMask).
// - The rest is jungle, the team's own or the enemy's depending on the side of the river (Radiant's side: x + y < 0).
// Checked by painting the regions over the minimap (work/sprint-19-map-regions).
public static class MapRegions
{
    // Same as replay-parser/internal/laning/zone.go (behind the tier-3 towers).
    private const int RadiantBaseMaxX = -4000;
    private const int RadiantBaseMaxY = -3700;
    private const int DireBaseMinX = 3700;
    private const int DireBaseMinY = 3300;

    // How far from a lane's path still counts as the lane (map units).
    public const int LaneHalfWidth = 1000;

    // Lane paths through the towers (zone.go): top along x ≈ -6450 then y ≈ 5950; bottom along y ≈ -6250 then
    // x ≈ 6350; middle on the diagonal between the tier-3 towers.
    private static readonly (int X, int Y)[] TopLane = [(-6450, -3700), (-6450, 5950), (3700, 5950)];
    private static readonly (int X, int Y)[] BottomLane = [(-4000, -6250), (6350, -6250), (6350, 3300)];
    private static readonly (int X, int Y)[] MiddleLane = [(-4640, -4144), (4272, 3759)];

    public static MapRegion RegionOf(Team team, int x, int y)
    {
        var isRadiant = team == Team.Radiant;
        if (x < RadiantBaseMaxX && y < RadiantBaseMaxY)
        {
            return isRadiant ? MapRegion.OwnBase : MapRegion.EnemyBase;
        }

        if (x > DireBaseMinX && y > DireBaseMinY)
        {
            return isRadiant ? MapRegion.EnemyBase : MapRegion.OwnBase;
        }

        // The nearest lane wins where two bands meet (never in practice: the lanes are far apart).
        var lanes = new[]
        {
            (Region: MapRegion.TopLane, Distance: DistanceToPath(TopLane, x, y)),
            (Region: MapRegion.MiddleLane, Distance: DistanceToPath(MiddleLane, x, y)),
            (Region: MapRegion.BottomLane, Distance: DistanceToPath(BottomLane, x, y)),
        };
        var nearest = lanes.MinBy(lane => lane.Distance);
        if (nearest.Distance <= LaneHalfWidth)
        {
            return nearest.Region;
        }

        if (IsWater(x, y))
        {
            return MapRegion.River;
        }

        var radiantSide = x + y < 0;
        return radiantSide == isRadiant ? MapRegion.OwnJungle : MapRegion.EnemyJungle;
    }

    // Shortest distance from the point to a path of straight segments.
    private static double DistanceToPath((int X, int Y)[] path, int x, int y)
    {
        var best = double.MaxValue;
        for (var i = 1; i < path.Length; i++)
        {
            var (ax, ay) = path[i - 1];
            var (bx, by) = path[i];
            double dx = bx - ax, dy = by - ay;
            // The point's projection on the segment, kept between its ends.
            var t = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy), 0, 1);
            var distance = Math.Sqrt(Math.Pow(x - (ax + t * dx), 2) + Math.Pow(y - (ay + t * dy), 2));
            best = Math.Min(best, distance);
        }

        return best;
    }

    private static bool IsWater(int x, int y)
    {
        var column = (x - RiverMask.Origin) / RiverMask.CellSize;
        var row = (-RiverMask.Origin - y) / RiverMask.CellSize;
        return row >= 0 && row < RiverMask.Rows.Length && column >= 0 && column < RiverMask.Rows[row].Length
            && RiverMask.Rows[row][column] == '#';
    }
}
