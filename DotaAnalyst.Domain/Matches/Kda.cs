namespace DotaAnalyst.Domain.Matches;

// Value object: kills, deaths and assists of a player in a match.
public record Kda(int Kills, int Deaths, int Assists);
