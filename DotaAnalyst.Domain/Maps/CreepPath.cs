using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Maps;

// The path one team's lane creeps walk (the map's path corners, from their barracks to the enemy Ancient).
public sealed record CreepPath(Lane Lane, Team Team, IReadOnlyList<MapPoint> Points)
{
    // Shortest distance from the point to the path (straight segments between the corners).
    public double DistanceTo(double x, double y)
    {
        if (Points.Count == 1)
        {
            return Math.Sqrt(Math.Pow(x - Points[0].X, 2) + Math.Pow(y - Points[0].Y, 2));
        }

        var best = double.MaxValue;
        for (var i = 1; i < Points.Count; i++)
        {
            var (a, b) = (Points[i - 1], Points[i]);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            var length = dx * dx + dy * dy;
            // The point's projection on the segment, kept between its ends.
            var t = length == 0 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / length, 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(x - (a.X + t * dx), 2) + Math.Pow(y - (a.Y + t * dy), 2)));
        }

        return best;
    }
}
