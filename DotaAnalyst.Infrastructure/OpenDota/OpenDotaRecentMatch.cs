namespace DotaAnalyst.Infrastructure.OpenDota;

// Only the fields we use from GET /players/{account_id}/recentMatches.
// JSON names are snake_case (match_id, player_slot...) and are mapped by OpenDotaClient's naming policy.
public record OpenDotaRecentMatch(
    long MatchId,
    int PlayerSlot,
    bool? RadiantWin,
    int Duration,
    int HeroId,
    long StartTime,
    int Kills,
    int Deaths,
    int Assists);
