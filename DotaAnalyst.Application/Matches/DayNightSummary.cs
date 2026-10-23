namespace DotaAnalyst.Application.Matches;

/// <summary>Kills and deaths by night, next to the totals.</summary>
/// <param name="NightShare">Share (0..1) of the match (from 0:00) that was night.</param>
/// <param name="Kills">Kills (killing blow or assist).</param>
/// <param name="KillsAtNight">Of those, the ones at night.</param>
/// <param name="Deaths">Deaths.</param>
/// <param name="DeathsAtNight">Of those, the ones at night.</param>
public record DayNightSummary(double NightShare, int Kills, int KillsAtNight, int Deaths, int DeathsAtNight);
