using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Candidate matches for the comparison base from OpenDota's lists.
// - Pro: GET /proMatches (league matches, Captain's Mode: roles are inferred in the parse).
// - High rank: GET /publicMatches?min_rank=80, only ranked All Pick (role queue), with at least MinRankedPlayers
//   public ranks (a lone ranked player makes a meaningless average) and a focus hero; paged to older matches.
// Only matches that ended ReplayDelay ago or more: Valve uploads the replay some time after the match.
public class OpenDotaBaselineMatchSource(OpenDotaClient openDotaClient, TimeProvider timeProvider) : IBaselineMatchSource
{
    public static readonly TimeSpan ReplayDelay = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    private const int RankedLobby = 7;
    private const int AllPickRanked = 22;
    private const int MinRankedPlayers = 3;
    // A page of public matches covers about half an hour: enough pages to reach matches older than ReplayDelay.
    private const int MaxPublicPages = 4;

    public async Task<IReadOnlyList<BaselineCandidate>> GetCandidatesAsync(
        BaselineSource source, IReadOnlySet<int> focusHeroes, CancellationToken cancellationToken)
    {
        var endedBefore = timeProvider.GetUtcNow() - ReplayDelay;
        var startedAfter = timeProvider.GetUtcNow() - MaxAge;

        bool Ready(long startTime, int duration) =>
            DateTimeOffset.FromUnixTimeSeconds(startTime + duration) <= endedBefore
            && DateTimeOffset.FromUnixTimeSeconds(startTime) >= startedAfter;

        if (source == BaselineSource.Pro)
        {
            return (await openDotaClient.GetProMatchesAsync(cancellationToken))
                .Where(match => Ready(match.StartTime, match.Duration))
                .Select(match => ToCandidate(match.MatchId, source))
                .OfType<BaselineCandidate>()
                .ToList();
        }

        if (focusHeroes.Count == 0)
        {
            return [];
        }

        var candidates = new List<BaselineCandidate>();
        long? lessThan = null;
        for (var page = 0; page < MaxPublicPages; page++)
        {
            var matches = await openDotaClient.GetHighRankPublicMatchesAsync(lessThan, cancellationToken);
            if (matches.Count == 0)
            {
                break;
            }

            candidates.AddRange(matches
                .Where(match => match.LobbyType == RankedLobby && match.GameMode == AllPickRanked
                    && match.NumRankTier >= MinRankedPlayers && Ready(match.StartTime, match.Duration)
                    && (match.RadiantTeam ?? []).Concat(match.DireTeam ?? []).Any(focusHeroes.Contains))
                .Select(match => ToCandidate(match.MatchId, source, [.. match.RadiantTeam ?? [], .. match.DireTeam ?? []]))
                .OfType<BaselineCandidate>());
            if (candidates.Count > 0)
            {
                break;
            }

            // At busy hours a page covers only a few minutes, so paging would never reach matches that ended
            // ReplayDelay ago. After the first page, jump straight to about JumpBack ago instead: match IDs grow
            // roughly linearly with time, and the page gives the rate.
            lessThan = page == 0 && EstimateIdAt(matches, timeProvider.GetUtcNow() - JumpBack) is { } jump
                ? jump
                : matches.Min(match => match.MatchId);
        }

        return candidates;
    }

    // ReplayDelay plus a typical match (40 min) and some slack: the page there has matches whose replays are up.
    public static readonly TimeSpan JumpBack = TimeSpan.FromMinutes(110);

    // The match ID that started about at that time, from the IDs and start times of one page; null when the page can't
    // tell (too few matches, or no time spread).
    internal static long? EstimateIdAt(IReadOnlyList<OpenDotaPublicMatch> page, DateTimeOffset at)
    {
        var newest = page.MaxBy(match => match.MatchId);
        var oldest = page.MinBy(match => match.MatchId);
        if (newest is null || oldest is null || newest.StartTime <= oldest.StartTime)
        {
            return null;
        }

        var idsPerSecond = (double)(newest.MatchId - oldest.MatchId) / (newest.StartTime - oldest.StartTime);
        var target = newest.MatchId - (long)(idsPerSecond * (newest.StartTime - at.ToUnixTimeSeconds()));
        return target > 0 && target < oldest.MatchId ? target : null;
    }

    private static BaselineCandidate? ToCandidate(long matchId, BaselineSource source, IReadOnlyList<int>? heroIds = null) =>
        MatchId.TryCreate(matchId, out var id) ? new BaselineCandidate(id, source, heroIds) : null;
}
