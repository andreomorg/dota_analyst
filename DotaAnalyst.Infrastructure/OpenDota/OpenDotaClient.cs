using System.Net.Http.Json;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Typed client: the HttpClient is created and configured by AddHttpClient<OpenDotaClient> in DependencyInjection.
public class OpenDotaClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Task<IReadOnlyList<OpenDotaRecentMatch>> GetRecentMatchesAsync(
        long accountId, CancellationToken cancellationToken)
    {
        return GetListAsync<OpenDotaRecentMatch>($"players/{accountId}/recentMatches", cancellationToken);
    }

    public Task<IReadOnlyList<OpenDotaHero>> GetHeroesAsync(CancellationToken cancellationToken)
    {
        return GetListAsync<OpenDotaHero>("heroes", cancellationToken);
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            var items = await httpClient.GetFromJsonAsync<List<T>>(path, JsonOptions, cancellationToken);
            return items ?? [];
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
