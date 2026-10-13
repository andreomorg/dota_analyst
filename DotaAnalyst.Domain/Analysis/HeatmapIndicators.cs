using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// Where and how a hero played, read from its heatmap (one phase, or several added up).
// - AliveSeconds: time alive with a position (dead time has no samples).
// - Advance: how far forward, on average, along the line from the hero's own fountain (0) to the enemy's (1); the
//   river is about 0.5.
// - Coverage: share of the map's playable cells the hero stood in at least twice (a sample is 2 s).
// - Concentration: share of the time spent in the hero's 5 most visited cells.
// - EnemySide: share of the time past the river (MatchMetricsCalculator.IsEnemySide, on the cells' centers).
// - NearAlly / Grouped: share of the time with 1+ / 2+ ally heroes within MatchMetricsCalculator.AllyRadius; null
//   for heatmaps made before it was counted.
public record PositionIndicators(
    int AliveSeconds, double Advance, double Coverage, double Concentration, double EnemySide, double? NearAlly, double? Grouped);

public static class HeatmapIndicators
{
    public const int SecondsPerSample = 2;
    public const int TopCells = 5;
    public const int MinSamplesToCover = 2;

    // The fountains on the line x + y (the map's diagonal): Radiant bottom-left, Dire top-right.
    private const double RadiantFountain = -6900;
    private const double DireFountain = 6700;

    // Cells whose center is on the playable map (the minimap's dark frame left out).
    private const int PlayableExtent = 8200;

    public static readonly int PlayableCells = Enumerable.Range(0, HeroHeatmap.GridSize)
        .SelectMany(column => Enumerable.Range(0, HeroHeatmap.GridSize).Select(row => (column, row)))
        .Count(cell => IsPlayable(cell.column, cell.row));

    // Null when the phases have no samples (the hero was dead or the match ended before them).
    public static PositionIndicators? Calculate(HeroHeatmap heatmap, Team team, params GamePhase[] phases)
    {
        ArgumentNullException.ThrowIfNull(heatmap);
        if (phases.Length == 0)
        {
            phases = [GamePhase.EarlyGame, GamePhase.MidGame, GamePhase.LateGame];
        }

        var cells = phases
            .SelectMany(phase => CellsOf(heatmap.Phases, phase))
            .GroupBy(cell => (cell.Column, cell.Row))
            .Select(group => new HeatCell(group.Key.Column, group.Key.Row, group.Sum(cell => cell.Samples)))
            .ToList();
        var samples = cells.Sum(cell => cell.Samples);
        if (samples == 0)
        {
            return null;
        }

        double Share(Func<HeatCell, bool> where) => (double)cells.Where(where).Sum(cell => cell.Samples) / samples;

        var advance = cells.Sum(cell =>
        {
            var (x, y) = Center(cell);
            var forward = Math.Clamp(((x + y) / 2.0 - RadiantFountain) / (DireFountain - RadiantFountain), 0, 1);
            return cell.Samples * (team == Team.Radiant ? forward : 1 - forward);
        }) / samples;
        var covered = cells.Count(cell => cell.Samples >= MinSamplesToCover && IsPlayable(cell.Column, cell.Row));
        var top = cells.OrderByDescending(cell => cell.Samples).Take(TopCells).Sum(cell => cell.Samples);

        double? near = null;
        double? grouped = null;
        if (heatmap.Proximity is { } proximity)
        {
            var counts = phases.Select(phase => ProximityOf(proximity, phase)).ToList();
            near = (double)counts.Sum(count => count.NearAlly) / samples;
            grouped = (double)counts.Sum(count => count.Grouped) / samples;
        }

        return new PositionIndicators(
            AliveSeconds: samples * SecondsPerSample,
            Advance: advance,
            Coverage: (double)covered / PlayableCells,
            Concentration: (double)top / samples,
            EnemySide: Share(cell =>
            {
                var (x, y) = Center(cell);
                return MatchMetricsCalculator.IsEnemySide(team, (int)x, (int)y);
            }),
            NearAlly: near,
            Grouped: grouped);
    }

    private static IReadOnlyList<HeatCell> CellsOf(PerGamePhase<IReadOnlyList<HeatCell>> phases, GamePhase phase) => phase switch
    {
        GamePhase.EarlyGame => phases.EarlyGame,
        GamePhase.MidGame => phases.MidGame,
        _ => phases.LateGame,
    };

    private static TeamProximity ProximityOf(PerGamePhase<TeamProximity> proximity, GamePhase phase) => phase switch
    {
        GamePhase.EarlyGame => proximity.EarlyGame,
        GamePhase.MidGame => proximity.MidGame,
        _ => proximity.LateGame,
    };

    private static (double X, double Y) Center(HeatCell cell) => Center(cell.Column, cell.Row);

    private static (double X, double Y) Center(int column, int row) =>
        (HeroHeatmap.MapMin + (column + 0.5) * HeroHeatmap.CellSize, HeroHeatmap.MapMax - (row + 0.5) * HeroHeatmap.CellSize);

    private static bool IsPlayable(int column, int row)
    {
        var (x, y) = Center(column, row);
        return Math.Abs(x) <= PlayableExtent && Math.Abs(y) <= PlayableExtent;
    }
}
