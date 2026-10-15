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
    IReadOnlyList<MatchPlayer> Players,
    // Where Valve keeps the replay file (it expires after a few weeks). Null when the source doesn't know it,
    // and for matches saved before this field existed.
    Uri? ReplayUrl = null,
    int SupplementalStatsVersion = 0,
    // Patch catalog identifier when provided by the source; null is unknown, not the current patch.
    // This does not identify a subpatch or certify historical mechanics.
    int? PatchId = null)
{
    // When the match ended (start + duration). Pauses count in Duration, so this is wall-clock time.
    public DateTimeOffset EndedAt => StartTime + Duration;

    public MatchPlayer? FindPlayer(AccountId accountId) =>
        Players.FirstOrDefault(player => player.AccountId == accountId);

    public bool? Won(MatchPlayer player) => MatchResult.Won(player.Team, WinningTeam);
}
