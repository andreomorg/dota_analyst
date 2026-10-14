using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Match details from GET /matches/{match_id}. A finished match doesn't change, but it gains
// detailed data once OpenDota parses the replay, so unparsed matches are cached for less time.
public class OpenDotaReplayAnalyzer(OpenDotaClient openDotaClient, IMemoryCache cache) : IReplayAnalyzer
{
    private static readonly TimeSpan ParsedCacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan UnparsedCacheDuration = TimeSpan.FromMinutes(5);
    // "Not found" is remembered briefly, so repeated requests for a wrong ID don't all reach OpenDota.
    private static readonly TimeSpan NotFoundCacheDuration = TimeSpan.FromMinutes(1);

    public Task<MatchDetails?> GetMatchDetailsAsync(MatchId matchId, CancellationToken cancellationToken) =>
        LoadAsync(matchId, refresh: false, cancellationToken);

    public Task<MatchDetails?> RefreshMatchDetailsAsync(MatchId matchId, CancellationToken cancellationToken) =>
        LoadAsync(matchId, refresh: true, cancellationToken);

    private async Task<MatchDetails?> LoadAsync(MatchId matchId, bool refresh, CancellationToken cancellationToken)
    {
        var cacheKey = $"opendota:match:{matchId.Value}";
        if (!refresh && cache.TryGetValue(cacheKey, out CachedMatch? cached) && cached is not null)
        {
            return cached.Details;
        }

        // Failures (exceptions) are not cached: the next request asks OpenDota again.
        var response = await openDotaClient.GetMatchAsync(matchId.Value, cancellationToken);
        if (response is null)
        {
            cache.Set(cacheKey, new CachedMatch(null), CacheEntries.NotFound(NotFoundCacheDuration));
            return null;
        }

        var details = response.ToMatchDetails();
        var duration = details.IsParsed ? ParsedCacheDuration : UnparsedCacheDuration;
        cache.Set(cacheKey, new CachedMatch(details), CacheEntries.Match(duration));
        return details;
    }

    // Wrapper so a cached "not found" (null details) can be told apart from "not in the cache".
    private sealed record CachedMatch(MatchDetails? Details);
}
