using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Typed client: the HttpClient is created and configured by AddHttpClient<OpenDotaClient> in DependencyInjection.
public class OpenDotaClient(HttpClient httpClient)
{
    // Internal so tests can deserialize JSON fixtures exactly like the client does.
    // Only property names are converted: dictionary keys (item IDs, benchmark names) are kept as they come.
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<IReadOnlyList<OpenDotaRecentMatch>> GetRecentMatchesAsync(
        long accountId, CancellationToken cancellationToken)
    {
        return await GetJsonAsync<List<OpenDotaRecentMatch>>(
            $"players/{accountId}/recentMatches", notFoundAsNull: false, cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<OpenDotaHero>> GetHeroesAsync(CancellationToken cancellationToken)
    {
        return await GetJsonAsync<List<OpenDotaHero>>("heroes", notFoundAsNull: false, cancellationToken) ?? [];
    }

    // Null when OpenDota answers 404: a match that doesn't exist is an expected result, not a failure.
    public Task<OpenDotaMatch?> GetMatchAsync(long matchId, CancellationToken cancellationToken)
    {
        return GetJsonAsync<OpenDotaMatch>($"matches/{matchId}", notFoundAsNull: true, cancellationToken);
    }

    // GET proMatches: the latest league matches (100), newest first.
    public async Task<IReadOnlyList<OpenDotaProMatch>> GetProMatchesAsync(CancellationToken cancellationToken)
    {
        return await GetJsonAsync<List<OpenDotaProMatch>>("proMatches", notFoundAsNull: false, cancellationToken) ?? [];
    }

    // GET publicMatches: 100 public matches of the highest ranks, newest first; lessThanMatchId pages to older ones.
    public async Task<IReadOnlyList<OpenDotaPublicMatch>> GetHighRankPublicMatchesAsync(
        long? lessThanMatchId, CancellationToken cancellationToken)
    {
        var path = lessThanMatchId is { } id ? $"publicMatches?min_rank=80&less_than_match_id={id}" : "publicMatches?min_rank=80";
        return await GetJsonAsync<List<OpenDotaPublicMatch>>(path, notFoundAsNull: false, cancellationToken) ?? [];
    }

    // GET constants/item_ids: { "1": "blink", ... } (item ID -> item key).
    public async Task<IReadOnlyDictionary<int, string>> GetItemIdsAsync(CancellationToken cancellationToken)
    {
        return await GetJsonAsync<Dictionary<int, string>>(
            "constants/item_ids", notFoundAsNull: false, cancellationToken) ?? [];
    }

    // GET constants/items: { "blink": { "dname": "Blink Dagger", ... }, ... } (item key -> item data).
    public async Task<IReadOnlyDictionary<string, OpenDotaItem>> GetItemsAsync(CancellationToken cancellationToken)
    {
        return await GetJsonAsync<Dictionary<string, OpenDotaItem>>(
            "constants/items", notFoundAsNull: false, cancellationToken) ?? [];
    }

    // Single place that turns every transport/format problem into DataSourceUnavailableException,
    // so the Application never sees HttpClient or JSON exceptions.
    private async Task<T?> GetJsonAsync<T>(string path, bool notFoundAsNull, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            // Default completion option (whole body buffered) on purpose: that way HttpClient.Timeout
            // also covers downloading the body, not only waiting for the headers.
            using var response = await httpClient.GetAsync(path, cancellationToken);

            if (notFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new DataSourceUnavailableException($"OpenDota request to '{path}' failed.", ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new DataSourceUnavailableException($"OpenDota returned an invalid response for '{path}'.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled without the caller asking for it: the HttpClient timeout expired.
            throw new DataSourceUnavailableException($"OpenDota request to '{path}' timed out.", ex);
        }
    }
}
