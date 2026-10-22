namespace DotaAnalyst.Domain.Maps;

// The map's own grid of square cells (the game's gridnav, 64 units in 7.41), rows from the bottom (y) up. Each cell has
// the gridnav flag byte and the height of its floor (null where there is none: walls, cliffs, outside the map).
public sealed class MapGrid
{
    // A floor height of short.MinValue means no floor.
    public const short NoFloor = short.MinValue;

    private readonly byte[] nav;
    private readonly short[] floor;

    public MapGrid(int cellSize, int width, int height, int originX, int originY, byte[] nav, short[] floor)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(floor);
        if (cellSize <= 0 || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "The grid needs a positive cell size and size.");
        }

        if (nav.Length != width * height || floor.Length != width * height)
        {
            throw new ArgumentException("The grid needs one flag and one floor height per cell.", nameof(nav));
        }

        (CellSize, Width, Height, OriginX, OriginY) = (cellSize, width, height, originX, originY);
        this.nav = nav;
        this.floor = floor;
    }

    public int CellSize { get; }
    public int Width { get; }
    public int Height { get; }
    public int OriginX { get; }
    public int OriginY { get; }

    // The gridnav flag byte of the cell under the point; null outside the grid.
    public byte? NavAt(double x, double y) => IndexOf(x, y) is { } index ? nav[index] : null;

    // The floor height under the point; null outside the grid or where the cell has no floor.
    public int? FloorAt(double x, double y) =>
        IndexOf(x, y) is { } index && floor[index] != NoFloor ? floor[index] : null;

    private int? IndexOf(double x, double y)
    {
        var column = (int)Math.Floor((x - OriginX) / CellSize);
        var row = (int)Math.Floor((y - OriginY) / CellSize);
        return column >= 0 && column < Width && row >= 0 && row < Height ? row * Width + column : null;
    }
}
