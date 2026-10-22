namespace DotaAnalyst.Domain.Maps;

// A trigger area of the map (a no-ward zone, a boss pit, a neutral camp, a shop): a box in the area's own space
// (LocalMin/LocalMax, around its origin) turned by Yaw degrees and placed at (X, Y). The pits are turned 182 and 342
// degrees, so a box along the axes would miss part of them.
public sealed record MapArea(string Name, int X, int Y, int Yaw, int LocalMinX, int LocalMinY, int LocalMaxX, int LocalMaxY)
{
    public bool Contains(double x, double y)
    {
        // Turn the point back by the yaw, around the origin, and test it against the box as it is in the model.
        var angle = -Yaw * Math.PI / 180;
        double dx = x - X, dy = y - Y;
        var localX = dx * Math.Cos(angle) - dy * Math.Sin(angle);
        var localY = dx * Math.Sin(angle) + dy * Math.Cos(angle);
        return localX >= LocalMinX && localX <= LocalMaxX && localY >= LocalMinY && localY <= LocalMaxY;
    }

    // The centre of the box, in the world.
    public (double X, double Y) Center
    {
        get
        {
            var angle = Yaw * Math.PI / 180;
            double cx = (LocalMinX + LocalMaxX) / 2.0, cy = (LocalMinY + LocalMaxY) / 2.0;
            return (X + cx * Math.Cos(angle) - cy * Math.Sin(angle), Y + cx * Math.Sin(angle) + cy * Math.Cos(angle));
        }
    }
}
