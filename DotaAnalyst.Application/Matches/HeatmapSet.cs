using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>Where each hero spent its time alive, as a grid over the map (column 0 = west edge, row 0 = north edge), per game phase.</summary>
/// <param name="GridSize">Columns and rows of the grid.</param>
/// <param name="MapMin">Map coordinate of the grid's west and south edges (the same square as the game's minimap image).</param>
/// <param name="MapMax">Map coordinate of the grid's east and north edges.</param>
/// <param name="SecondsPerSample">About how many seconds of game each sample stands for.</param>
/// <param name="Heroes">One heatmap per hero, Radiant first.</param>
/// <param name="MatchAverage">The average of the ten heroes' indicators, per phase.</param>
public record HeatmapSet(
    int GridSize, int MapMin, int MapMax, int SecondsPerSample, IReadOnlyList<HeroHeatmapView> Heroes,
    PhaseIndicators? MatchAverage = null);
