using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Domain.Matches;

// Aggregate root: a whole match with its ten players.
public record MatchDetails(
    MatchId MatchId,
    TimeSpan Duration,
    DateTimeOffset StartTime,
    // Null when the match result is not known yet.
    Team? WinningTeam,
    int RadiantScore,
    int DireScore,
    // True when the replay was parsed (detailed data such as timelines is available).
    bool IsParsed,
    IReadOnlyList<MatchPlayer> Players)
{
    public MatchPlayer? FindPlayer(AccountId accountId) =>
        Players.FirstOrDefault(player => player.AccountId == accountId);

    public bool? Won(MatchPlayer player) => MatchResult.Won(player.Team, WinningTeam);
}
