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
                .Select(match => ToCandidate(match.MatchId, source))
                .OfType<BaselineCandidate>());
            if (candidates.Count > 0)
            {
                break;
            }

            lessThan = matches.Min(match => match.MatchId);
        }

        return candidates;
    }

    private static BaselineCandidate? ToCandidate(long matchId, BaselineSource source) =>
        MatchId.TryCreate(matchId, out var id) ? new BaselineCandidate(id, source) : null;
}
