namespace DotaAnalyst.Domain.Matches;

// A match from the point of view of one player.
public record PlayerMatch(
    long MatchId,
    int HeroId,
    Team Team,
    // Null when the match result is not known yet.
    Team? WinningTeam,
    Kda Kda,
    TimeSpan Duration,
    DateTimeOffset StartTime)
{
    public bool? Won => MatchResult.Won(Team, WinningTeam);
}
