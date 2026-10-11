using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using Microsoft.Extensions.Caching.Memory;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Match details from GET /matches/{match_id}. A finished match doesn't change, but it gains
// detailed data once OpenDota parses the replay, so unparsed matches are cached for less time.
public class OpenDotaReplayAnalyzer(OpenDotaClient openDotaClient, IMemoryCache cache) : IReplayAnalyzer
{
    private static readonly TimeSpan ParsedCacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan UnparsedCacheDuration = TimeSpan.FromMinutes(5);

    public async Task<MatchDetails?> GetMatchDetailsAsync(MatchId matchId, CancellationToken cancellationToken)
    {
        var cacheKey = $"opendota:match:{matchId.Value}";
        if (cache.TryGetValue(cacheKey, out MatchDetails? cached) && cached is not null)
        {
            return cached;
        }

        // Not found (null) and failures (exceptions) are not cached: the next request asks OpenDota again.
        var response = await openDotaClient.GetMatchAsync(matchId.Value, cancellationToken);
        if (response is null)
        {
            return null;
        }

        var details = response.ToMatchDetails();
        cache.Set(cacheKey, details, details.IsParsed ? ParsedCacheDuration : UnparsedCacheDuration);
        return details;
    }
}
