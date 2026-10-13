using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// One square of the heatmap grid and how many position samples of the hero fell in it.
// Column 0 is the west edge of the map, row 0 the north edge (like an image of the minimap).
public readonly record struct HeatCell(int Column, int Row, int Samples);

// Where one hero spent its time alive, as a GridSize x GridSize grid over the whole map, per game phase. It is how
// much the positions are worth keeping: a few hundred numbers instead of a sample every 2 s, so it can be saved
// (and mirrored to production) while the positions never are.
public sealed class HeroHeatmap
{
    public const int GridSize = 32;

    // The square the grid covers: the area of the game's own minimap image (resource/overviews/dota.txt:
    // pos_x -9472, pos_y 9472, 18.5 units per pixel of a 1024 px image), so a cell lines up with the minimap.
    public const int MapMin = -9472;
    public const int MapMax = 9472;
    public const double CellSize = (double)(MapMax - MapMin) / GridSize;

    public HeroHeatmap(int heroId, PerGamePhase<IReadOnlyList<HeatCell>> phases)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        ArgumentNullException.ThrowIfNull(phases);

        HeroId = heroId;
        Phases = phases.Select(cells => Validated(cells));
    }

    public int HeroId { get; }

    // Only the cells with samples, ordered by row then column.
    public PerGamePhase<IReadOnlyList<HeatCell>> Phases { get; }

    // The cell of a map position; positions off the grid are clamped to its edge.
    public static (int Column, int Row) CellOf(int x, int y) =>
        (Clamp((int)Math.Floor((x - MapMin) / CellSize)), Clamp((int)Math.Floor((MapMax - y) / CellSize)));

    private static int Clamp(int index) => Math.Clamp(index, 0, GridSize - 1);

    private static IReadOnlyList<HeatCell> Validated(IReadOnlyList<HeatCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        foreach (var cell in cells)
        {
            if (cell.Column is < 0 or >= GridSize || cell.Row is < 0 or >= GridSize)
            {
                throw new ArgumentException($"A heatmap cell ({cell.Column}, {cell.Row}) is off the grid.", nameof(cells));
            }

            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cell.Samples, nameof(cells));
        }

        if (cells.DistinctBy(cell => (cell.Column, cell.Row)).Count() != cells.Count)
        {
            throw new ArgumentException("A heatmap cell appears twice.", nameof(cells));
        }

        return cells.OrderBy(cell => cell.Row).ThenBy(cell => cell.Column).ToList();
    }

    // Counts the samples of each cell, split by game phase like the other per-phase metrics (0-10, 10-20, 20+ min).
    // Samples before 0:00 (heroes walking to their lanes) are left out.
    public static HeroHeatmap FromPositions(HeroPositions positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var early = new Dictionary<(int, int), int>();
        var mid = new Dictionary<(int, int), int>();
        var late = new Dictionary<(int, int), int>();
        foreach (var sample in positions.Samples.Where(sample => sample.Seconds >= 0))
        {
            var phase = sample.Seconds < 600 ? early : sample.Seconds < 1200 ? mid : late;
            var cell = CellOf(sample.X, sample.Y);
            phase[cell] = phase.GetValueOrDefault(cell) + 1;
        }

        static IReadOnlyList<HeatCell> Cells(Dictionary<(int Column, int Row), int> counts) =>
            counts.Select(entry => new HeatCell(entry.Key.Column, entry.Key.Row, entry.Value)).ToList();

        return new HeroHeatmap(positions.HeroId, new PerGamePhase<IReadOnlyList<HeatCell>>(Cells(early), Cells(mid), Cells(late)));
    }
}
