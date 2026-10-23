using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>Position indicators of one hero in one period.</summary>
/// <param name="AliveSeconds">Time alive with a known position, in seconds.</param>
/// <param name="Advance">How far forward on average, from the own fountain (0) to the enemy's (1); the river is about 0.5.</param>
/// <param name="Coverage">Share of the playable map the hero stood in (cells of about 590 units, at least 4 s in each).</param>
/// <param name="Concentration">Share of the time in the 5 most visited cells.</param>
/// <param name="EnemySide">Share of the time past the river, on the enemy's side.</param>
/// <param name="NearAlly">Share of the time with at least one ally hero within 1500 units; null for older analyses.</param>
/// <param name="Grouped">Share of the time with at least two ally heroes within 1500 units; null for older analyses.</param>
public record PositionIndicatorsView(
    int AliveSeconds, double Advance, double Coverage, double Concentration, double EnemySide, double? NearAlly, double? Grouped);
