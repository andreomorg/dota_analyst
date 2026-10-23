namespace DotaAnalyst.Application.Matches;

/// <summary>The Smokes of Deceit the hero used.</summary>
/// <param name="Smokes">Smokes used.</param>
/// <param name="SmokesWithKill">Of those, the ones followed by an enemy hero death within 60 s.</param>
public record SmokesSummary(int Smokes, int SmokesWithKill);
