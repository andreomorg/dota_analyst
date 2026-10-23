namespace DotaAnalyst.Domain.Maps;

// Conservative ground route approximation. Null means unavailable, unreachable or budget exhausted.
public static class MapPathfinder
{
    public const int Spacing = 256;
    public const int DefaultMaxExpandedNodes = 20_000;

    public static IReadOnlyList<MapPoint>? Find(GameMap? map, MapPoint start, MapPoint destination,
        IReadOnlySet<int>? downTrees, int maxExpandedNodes = DefaultMaxExpandedNodes)
    {
        if (map is null || downTrees is null || map.Trees.Count == 0 || maxExpandedNodes <= 0
            || !double.IsFinite(start.X) || !double.IsFinite(start.Y)
            || !double.IsFinite(destination.X) || !double.IsFinite(destination.Y))
            return null;

        bool Clear(MapPoint a, MapPoint b)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / Math.Min(32.0, map.Grid.CellSize / 4.0)));
            for (var i = 0; i <= steps; i++)
            {
                var x = a.X + (b.X - a.X) * i / steps;
                var y = a.Y + (b.Y - a.Y) * i / steps;
                if (!map.IsWalkable(x, y) || map.IsTreeAt(x, y, downTrees)) return false;
            }
            return true;
        }

        MapPoint Point((int X, int Y) cell) => new(start.X + cell.X * Spacing, start.Y + cell.Y * Spacing);
        if (!Clear(start, start) || !Clear(destination, destination)) return null;
        var goal = ((int)Math.Round((destination.X - start.X) / Spacing), (int)Math.Round((destination.Y - start.Y) / Spacing));
        if (!Clear(Point(goal), destination)) return null;
        var queue = new PriorityQueue<((int X, int Y) Cell, double Cost), double>();
        var costs = new Dictionary<(int, int), double> { [(0, 0)] = 0 };
        var parents = new Dictionary<(int, int), (int, int)>();
        queue.Enqueue(((0, 0), 0), start.DistanceTo(Point(goal)));
        var expanded = 0;
        while (queue.TryDequeue(out var entry, out _))
        {
            var cell = entry.Cell;
            if (entry.Cost > costs[cell]) continue;
            if (++expanded > maxExpandedNodes) return null;
            if (cell == goal)
            {
                var path = new List<MapPoint> { Point(cell) };
                while (parents.TryGetValue(cell, out var parent)) { cell = parent; path.Add(Point(cell)); }
                path.Reverse();
                if (path[^1] != destination) path.Add(destination);
                return path.AsReadOnly();
            }
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var next = (cell.X + dx, cell.Y + dy);
                var cost = entry.Cost + Spacing * (dx != 0 && dy != 0 ? Math.Sqrt(2) : 1);
                if (costs.TryGetValue(next, out var known) && known <= cost) continue;
                // Prevent diagonal corner cutting as well as walls/trees between coarse nodes.
                if (!Clear(Point(cell), Point(next)) || (dx != 0 && dy != 0
                    && (!Clear(Point(cell), Point((cell.X + dx, cell.Y))) || !Clear(Point(cell), Point((cell.X, cell.Y + dy)))))) continue;
                costs[next] = cost;
                parents[next] = cell;
                queue.Enqueue((next, cost), cost + Point(next).DistanceTo(Point(goal)));
            }
        }
        return null;
    }
}
