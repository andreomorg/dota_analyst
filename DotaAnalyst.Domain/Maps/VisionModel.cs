namespace DotaAnalyst.Domain.Maps;

// Something that gives its team vision at a moment: a hero, a ward, a building, a creep. Radius in map units (the
// day or night value of the unit). Flying vision ignores terrain, trees and blockers.
public readonly record struct VisionSource(double X, double Y, double Radius, bool Flying = false);

// Which obstacles block vision (all by default); turning them off measures what each one adds (MP4).
// TreeRadius: a standing tree blocks a line passing this close to its centre (null: the 2 x 2 cells around it).
// HeightStep: terrain blocks when it is at least this much higher than the source's floor (null: by whole levels, 128).
// EndClearance: cells whose centre is this close to the source or the target never block (a hero next to a tree line
// or at the foot of a cliff). TargetSlack: the target's terrain level is the lowest within this distance of it (a
// cell's floor is the highest flat part in it, so a hero at the foot of a cliff can fall in a "high" cell).
// RadiusBonus: added to every source's range (the game computes vision on its own grid).
public sealed record VisionRules(bool Terrain = true, bool Trees = true, bool FogBlockers = true, double? TreeRadius = null,
    int? HeightStep = null, double EndClearance = 0, double TargetSlack = 0, double RadiusBonus = 0)
{
    public static VisionRules All { get; } = new();

    public static VisionRules RadiusOnly { get; } = new(false, false, false);

    // The rules measured against the real fog of war of 1,158 matches (MP4, 22/10/2026: 94.6% of the heroes' samples
    // right, 97.1% of "seen" right, with the moving vision units).
    public static VisionRules Measured { get; } = new(TargetSlack: 64);
}

// Domain service: whether a vision source sees a point, by the map of the patch. The game's rules, as far as the map
// file allows: a ground unit sees no terrain higher than its own; standing trees and fog blockers on the way hide what
// is behind them. Walked over the map's grid (64 units): every cell the line crosses between the two, without the
// source's and the target's own cells. An approximation of the game's vision (which uses its own grid and shapes):
// measured against the replay's real fog of war before any use (work/agents/plan.md MP4).
public static class VisionModel
{
    public static bool Sees(GameMap map, VisionSource source, double x, double y, IReadOnlySet<int>? downTrees = null,
        VisionRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        rules ??= VisionRules.All;
        double dx = x - source.X, dy = y - source.Y;
        var radius = source.Radius + rules.RadiusBonus;
        if (dx * dx + dy * dy > radius * radius)
        {
            return false;
        }

        if (source.Flying || rules is { Terrain: false, Trees: false, FogBlockers: false })
        {
            return true;
        }

        // Terrain compares whole levels, or floor heights with a step.
        var sourceFloor = rules.HeightStep is null ? map.LevelAt(source.X, source.Y) : map.FloorAt(source.X, source.Y);
        bool Higher(double px, double py)
        {
            var floor = rules.HeightStep is null ? map.LevelAt(px, py) : map.FloorAt(px, py);
            return sourceFloor is { } from && floor is { } to && to - from >= (rules.HeightStep ?? 1);
        }

        if (rules.Terrain && Higher(x, y) && (rules.TargetSlack <= 0 || Around(x, y, rules.TargetSlack).All(point => Higher(point.X, point.Y))))
        {
            return false;
        }

        if (rules.Trees && rules.TreeRadius is { } treeRadius && TreeOnTheWay(map, source, x, y, treeRadius, downTrees))
        {
            return false;
        }

        var start = map.CellOf(source.X, source.Y);
        var end = map.CellOf(x, y);
        foreach (var cell in CellsBetween(map, source.X, source.Y, x, y))
        {
            if (cell == start || cell == end)
            {
                continue;
            }

            if (rules.EndClearance > 0)
            {
                var (cx, cy) = map.CenterOf(cell);
                if (Near(cx, cy, source.X, source.Y, rules.EndClearance) || Near(cx, cy, x, y, rules.EndClearance))
                {
                    continue;
                }
            }

            if (rules.Trees && rules.TreeRadius is null && map.HasStandingTree(cell, downTrees))
            {
                return false;
            }

            if (rules.FogBlockers && map.HasFogBlocker(cell))
            {
                return false;
            }

            if (rules.Terrain)
            {
                var (cx, cy) = map.CenterOf(cell);
                if (Higher(cx, cy))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Near(double ax, double ay, double bx, double by, double distance) =>
        (ax - bx) * (ax - bx) + (ay - by) * (ay - by) <= distance * distance;

    // The point and eight around it at the given distance.
    private static IEnumerable<(double X, double Y)> Around(double x, double y, double distance)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                yield return (x + dx * distance, y + dy * distance);
            }
        }
    }

    // A standing tree whose centre is within radius of the line, between the two ends (not right at either).
    private static bool TreeOnTheWay(GameMap map, VisionSource source, double x, double y, double radius, IReadOnlySet<int>? downTrees)
    {
        double dx = x - source.X, dy = y - source.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1)
        {
            return false;
        }

        foreach (var index in map.TreesWithin((source.X + x) / 2, (source.Y + y) / 2, length / 2 + radius, downTrees))
        {
            var tree = map.Trees[index];
            // How far along the line the tree is, and how far from it.
            var along = ((tree.X - source.X) * dx + (tree.Y - source.Y) * dy) / length;
            if (along <= radius || along >= length - radius)
            {
                continue;
            }

            var across = Math.Abs((tree.X - source.X) * dy - (tree.Y - source.Y) * dx) / length;
            if (across < radius)
            {
                return true;
            }
        }

        return false;
    }

    // The cells along the segment, in order, sampled every quarter cell (no cell crossed is skipped by more than a
    // corner's width).
    private static IEnumerable<(int Column, int Row)> CellsBetween(GameMap map, double x0, double y0, double x1, double y1)
    {
        var length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        var steps = Math.Max(1, (int)Math.Ceiling(length / (map.Grid.CellSize / 4.0)));
        (int, int)? previous = null;
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var cell = map.CellOf(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
            if (cell != previous)
            {
                previous = cell;
                yield return cell;
            }
        }
    }
}
