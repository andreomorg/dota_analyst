using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Maps;

// The static map of one patch, read from the game's own map file (tools/MapExtractor): terrain, trees, fog blockers,
// trigger areas, creep paths and landmarks. Coordinates are the replay's. Only valid for its patch: a match of another
// patch gets no map (unknown, never guessed). What changes during a match (trees cut, the active pit, who holds an
// outpost) comes from the replay and is passed in.
public sealed class GameMap
{
    // Floor heights come in steps of 128 (0 river, 128 low ground, 256 high ground, 384 fountains; the Tormentor's
    // area is below the river).
    public const int LevelHeight = 128;

    // A tree stands on the corner of four 64-unit cells and blocks the 128 x 128 square around it.
    public const int TreeHalfSize = 64;

    // Trees are found through buckets of this size.
    private const int TreeBucket = 512;

    private readonly Dictionary<(int, int), List<int>> treesByBucket = [];

    // The trees covering each grid cell (a tree covers the four cells around its corner) and the cells with a fog
    // blocker, for line-of-sight walks over the grid.
    private readonly Dictionary<(int Column, int Row), List<int>> treesByCell = [];
    private readonly HashSet<(int Column, int Row)> fogBlockerCells = [];

    public GameMap(string patch, MapGrid grid, IReadOnlyList<MapPoint> trees, IReadOnlyList<MapPoint> fogBlockers,
        IReadOnlyList<MapArea> noWardZones, IReadOnlyList<MapArea> bossPits, IReadOnlyList<MapArea> campAreas,
        IReadOnlyList<MapArea> shopAreas, IReadOnlyList<CreepPath> creepPaths, IReadOnlyList<MapLandmark> landmarks,
        IReadOnlyList<MapPoint>? fogRevealers = null, IReadOnlyList<MapPoint>? currents = null, MapSource? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patch);
        (Patch, Grid, Trees, FogBlockers, NoWardZones, BossPits, CampAreas, ShopAreas, CreepPaths, Landmarks) =
            (patch, grid, trees, fogBlockers, noWardZones, bossPits, campAreas, shopAreas, creepPaths, landmarks);
        (FogRevealers, Currents, Source) = (fogRevealers ?? [], currents ?? [], source);
        for (var index = 0; index < trees.Count; index++)
        {
            var key = Bucket(trees[index].X, trees[index].Y);
            if (!treesByBucket.TryGetValue(key, out var list))
            {
                treesByBucket[key] = list = [];
            }

            list.Add(index);

            foreach (var dx in new[] { -TreeHalfSize / 2, TreeHalfSize / 2 })
            {
                foreach (var dy in new[] { -TreeHalfSize / 2, TreeHalfSize / 2 })
                {
                    var cell = CellOf(trees[index].X + dx, trees[index].Y + dy);
                    if (!treesByCell.TryGetValue(cell, out var cellTrees))
                    {
                        treesByCell[cell] = cellTrees = [];
                    }

                    cellTrees.Add(index);
                }
            }
        }

        foreach (var blocker in fogBlockers)
        {
            fogBlockerCells.Add(CellOf(blocker.X, blocker.Y));
        }
    }

    // The grid cell (column, row) of a point.
    public (int Column, int Row) CellOf(double x, double y) =>
        ((int)Math.Floor((x - Grid.OriginX) / Grid.CellSize), (int)Math.Floor((y - Grid.OriginY) / Grid.CellSize));

    // The centre of a grid cell.
    public (double X, double Y) CenterOf((int Column, int Row) cell) =>
        (Grid.OriginX + (cell.Column + 0.5) * Grid.CellSize, Grid.OriginY + (cell.Row + 0.5) * Grid.CellSize);

    // Whether a standing tree covers the cell (down: trees cut or destroyed, from the replay).
    public bool HasStandingTree((int Column, int Row) cell, IReadOnlySet<int>? down = null) =>
        treesByCell.TryGetValue(cell, out var trees) && trees.Any(tree => down?.Contains(tree) != true);

    // Whether a fog-of-war blocker stands in the cell.
    public bool HasFogBlocker((int Column, int Row) cell) => fogBlockerCells.Contains(cell);

    // A base's plateau starts at this floor height (the bases stand at 256; ramps are lower).
    public const int BaseFloor = 240;

    private Dictionary<(int Column, int Row), Team>? baseCells;

    // The team whose base the point is in: the high plateau connected to its Ancient (walkable grid cells with a floor
    // at BaseFloor or higher, reached from around the Ancient through side neighbours). Null outside both bases.
    public Team? BaseAt(double x, double y)
    {
        baseCells ??= FindBases();
        return baseCells.TryGetValue(CellOf(x, y), out var team) ? team : null;
    }

    private Dictionary<(int Column, int Row), Team> FindBases()
    {
        var cells = new Dictionary<(int Column, int Row), Team>();
        foreach (var ancient in Landmarks.Where(landmark => landmark.Kind == LandmarkKind.Ancient && landmark.Team is not null))
        {
            // The Ancient's own cells are a building (no floor): the fill starts from the plateau around it.
            var start = CellOf(ancient.X, ancient.Y);
            var queue = new Queue<(int Column, int Row)>();
            for (var dc = -4; dc <= 4; dc++)
            {
                for (var dr = -4; dr <= 4; dr++)
                {
                    queue.Enqueue((start.Column + dc, start.Row + dr));
                }
            }

            while (queue.TryDequeue(out var cell))
            {
                var (cx, cy) = CenterOf(cell);
                // Walkable only: the high ground outside the playable area would carry the fill around the map.
                if (cells.ContainsKey(cell) || Grid.FloorAt(cx, cy) is not >= BaseFloor || !IsWalkable(cx, cy))
                {
                    continue;
                }

                cells[cell] = ancient.Team!.Value;
                foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    queue.Enqueue((cell.Column + dc, cell.Row + dr));
                }
            }
        }

        return cells;
    }

    // The patch this map belongs to (e.g. "7.41").
    public string Patch { get; }
    public MapGrid Grid { get; }
    // Every tree of the map at the start of a match, in the map file's order. The index is the tree's bit in the
    // replay's m_bWorldTreeState (timelines.trees: checked on two 7.41 replays), so the replay's cut trees are indexes here.
    public IReadOnlyList<MapPoint> Trees { get; }
    public IReadOnlyList<MapPoint> FogBlockers { get; }
    public IReadOnlyList<MapArea> NoWardZones { get; }
    // The two Roshan pits (the active one changes during a match: see PitNearest).
    public IReadOnlyList<MapArea> BossPits { get; }
    public IReadOnlyList<MapArea> CampAreas { get; }
    public IReadOnlyList<MapArea> ShopAreas { get; }
    public IReadOnlyList<CreepPath> CreepPaths { get; }
    public IReadOnlyList<MapLandmark> Landmarks { get; }

    // Fog revealers placed in the map file (ent_fow_revealer): present in the file does not mean active in a match.
    public IReadOnlyList<MapPoint> FogRevealers { get; }

    // The river currents' path points (dota_movespeed_modifier_path: up to +150 move speed in 7.41).
    public IReadOnlyList<MapPoint> Currents { get; }

    // The map file this map was extracted from; null when the map does not say.
    public MapSource? Source { get; }

    // The floor height under the point; null on walls, cliffs and outside the map.
    public int? FloorAt(double x, double y) => Grid.FloorAt(x, y);

    // The terrain level under the point: 0 river, 1 low ground, 2 high ground, 3 fountains (negative below the river).
    // Higher ground is what blocks vision from below.
    public int? LevelAt(double x, double y) =>
        FloorAt(x, y) is { } floor ? (int)Math.Round(floor / (double)LevelHeight, MidpointRounding.AwayFromZero) : null;

    // Whether a unit can stand there, by the gridnav (trees are not in it: see IsTreeAt). Bit 0x01 of the flag byte;
    // 0x10 alone is a cliff or wall, 0x14 outside the playable area.
    public bool IsWalkable(double x, double y) => Grid.NavAt(x, y) is { } flags && (flags & 0x01) != 0;

    // The indexes of the trees within radius of the point (centre to centre), leaving out those in down (cut or
    // destroyed: from the replay; null = the map as it starts).
    public IReadOnlyList<int> TreesWithin(double x, double y, double radius, IReadOnlySet<int>? down = null)
    {
        var found = new List<int>();
        var (fromX, fromY) = Bucket(x - radius, y - radius);
        var (toX, toY) = Bucket(x + radius, y + radius);
        for (var bx = fromX; bx <= toX; bx++)
        {
            for (var by = fromY; by <= toY; by++)
            {
                if (!treesByBucket.TryGetValue((bx, by), out var list))
                {
                    continue;
                }

                foreach (var index in list)
                {
                    var tree = Trees[index];
                    if (down?.Contains(index) != true && new MapPoint(tree.X, tree.Y).IsWithin(new MapPoint(x, y), radius))
                    {
                        found.Add(index);
                    }
                }
            }
        }

        return found;
    }

    // Whether a standing tree covers the point.
    public bool IsTreeAt(double x, double y, IReadOnlySet<int>? down = null) =>
        TreesWithin(x, y, TreeHalfSize * Math.Sqrt(2), down).Any(index =>
            Math.Abs(Trees[index].X - x) <= TreeHalfSize && Math.Abs(Trees[index].Y - y) <= TreeHalfSize);

    public bool InNoWardZone(double x, double y) => NoWardZones.Any(zone => zone.Contains(x, y));

    // The Roshan pit the point is in, if any.
    public MapArea? PitAt(double x, double y) => BossPits.FirstOrDefault(pit => pit.Contains(x, y));

    // The pit nearest to a point: given Roshan's own position from the replay, the active pit.
    public MapArea? PitNearest(double x, double y) => BossPits.MinBy(pit => new MapPoint(pit.Center.X, pit.Center.Y).DistanceSquaredTo(new MapPoint(x, y)));

    public MapArea? CampAt(double x, double y) => CampAreas.FirstOrDefault(camp => camp.Contains(x, y));

    public MapArea? ShopAt(double x, double y) => ShopAreas.FirstOrDefault(shop => shop.Contains(x, y));

    // The creep path nearest to the point (of one team's creeps, or of either) and how far it is.
    public (CreepPath Path, double Distance)? NearestCreepPath(double x, double y, Team? team = null) =>
        CreepPaths.Where(path => team is null || path.Team == team)
            .Select(path => (Path: path, Distance: path.DistanceTo(x, y)))
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => ((CreepPath, double)?)candidate)
            .FirstOrDefault();

    // The nearest landmark of a kind (of one team, or of any) and how far it is.
    public (MapLandmark Landmark, double Distance)? NearestLandmark(LandmarkKind kind, double x, double y, Team? team = null) =>
        Landmarks.Where(landmark => landmark.Kind == kind && (team is null || landmark.Team == team))
            .Select(landmark => (Landmark: landmark, Distance: new MapPoint(landmark.X, landmark.Y).DistanceTo(new MapPoint(x, y))))
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => ((MapLandmark, double)?)candidate)
            .FirstOrDefault();

    private static (int, int) Bucket(double x, double y) =>
        ((int)Math.Floor(x / TreeBucket), (int)Math.Floor(y / TreeBucket));
}
