namespace DotaAnalyst.Domain.Maps;

// Value object: a point of the map in the game's own coordinates (the replay's x/y, roughly -8200..8200 on both axes,
// Radiant's base bottom-left). Distances are straight lines in map units; "within" compares squared distances, so no
// square root is taken where only a comparison is needed.
public readonly record struct MapPoint(double X, double Y)
{
    public double DistanceTo(MapPoint other) => Math.Sqrt(DistanceSquaredTo(other));

    public double DistanceSquaredTo(MapPoint other) => (X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y);

    // At most radius away (the edge counts as within).
    public bool IsWithin(MapPoint other, double radius) => DistanceSquaredTo(other) <= radius * radius;
}
