namespace DotaAnalyst.Domain.Matches;

public static class MatchResult
{
    // A player won when their team is the winning team; null when the result is not known yet.
    public static bool? Won(Team team, Team? winningTeam) => winningTeam is null ? null : winningTeam == team;
}
