namespace DotaAnalyst.Application.Matches;

/// <summary>What the hero farmed (creeps it got the last hit on).</summary>
/// <param name="LaneCreeps">Lane creeps.</param>
/// <param name="Neutrals">Neutral creeps.</param>
/// <param name="Denies">Own creeps denied.</param>
/// <param name="LaneCreepsAfterLaning">Lane creeps from 10:00 on.</param>
/// <param name="NeutralsAfterLaning">Neutral creeps from 10:00 on.</param>
public record FarmSummary(int LaneCreeps, int Neutrals, int Denies, int LaneCreepsAfterLaning, int NeutralsAfterLaning);
