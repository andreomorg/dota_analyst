namespace DotaAnalyst.Application.Matches;

/// <summary>A recent match of a player (one row of the matches list).</summary>
/// <param name="MatchId">Dota match ID.</param>
/// <param name="HeroId">Hero played, as OpenDota's hero ID.</param>
/// <param name="HeroName">Hero name, or "Unknown hero" when the catalog doesn't know it.</param>
/// <param name="Kills">Kills of the player.</param>
/// <param name="Deaths">Deaths of the player.</param>
/// <param name="Assists">Assists of the player.</param>
/// <param name="DurationSeconds">Match duration, in seconds.</param>
/// <param name="StartTime">When the match started (UTC).</param>
/// <param name="IsRadiant">True when the player was on Radiant, false on Dire.</param>
/// <param name="Won">Whether the player won; null when the result is not known.</param>
public record MatchSummary(
    long MatchId,
    int HeroId,
    string HeroName,
    int Kills,
    int Deaths,
    int Assists,
    int DurationSeconds,
    DateTimeOffset StartTime,
    bool IsRadiant,
    bool? Won);
