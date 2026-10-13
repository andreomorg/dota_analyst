namespace DotaAnalyst.Infrastructure.OpenDota;

// Only the fields we use from GET /proMatches (league matches, newest first).
public record OpenDotaProMatch(long MatchId, long StartTime, int Duration);

// Only the fields we use from GET /publicMatches (public matches, newest first). avg_rank_tier is the average of the
// num_rank_tier players with a public rank; radiant_team/dire_team are hero IDs.
public record OpenDotaPublicMatch(
    long MatchId,
    long StartTime,
    int Duration,
    int? LobbyType,
    int? GameMode,
    int? AvgRankTier,
    int? NumRankTier,
    IReadOnlyList<int>? RadiantTeam,
    IReadOnlyList<int>? DireTeam);
