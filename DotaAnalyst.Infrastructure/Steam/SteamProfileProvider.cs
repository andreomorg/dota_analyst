using System.Net.Http.Json;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.Steam;

// Steam profiles from the Steam Web API (GetPlayerSummaries). Typed HttpClient, configured in DependencyInjection;
// its request logging is turned off there, because the API key travels in the URL and must never reach the logs.
public class SteamProfileProvider(HttpClient httpClient, IOptions<SteamOptions> options, IMemoryCache cache)
    : ISteamProfileProvider
{
    // Names and avatars change rarely; an hour keeps us far from the Steam Web API limits.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PlayerProfile?> GetProfileAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        var cacheKey = $"steam:profile:{accountId.Value}";
        if (cache.TryGetValue(cacheKey, out CachedProfile? cached) && cached is not null)
        {
            return cached.Profile;
        }

        var steamId64 = accountId.SteamId64.ToString();
        var key = Uri.EscapeDataString(options.Value.ApiKey ?? "");
        var response = await GetJsonAsync($"ISteamUser/GetPlayerSummaries/v2/?key={key}&steamids={steamId64}", cancellationToken);

        var profile = ToProfile(response?.Response?.Players?.FirstOrDefault(player => player.Steamid == steamId64));
        cache.Set(cacheKey, new CachedProfile(profile), CacheEntries.Profile(CacheDuration));
        return profile;
    }

    internal static PlayerProfile? ToProfile(SteamPlayerSummary? player)
    {
        if (string.IsNullOrWhiteSpace(player?.Personaname))
        {
            return null;
        }

        // Only an absolute https address is used as an image source in the page.
        var avatar = Uri.TryCreate(player.Avatarfull, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : null;
        return new PlayerProfile(player.Personaname, avatar);
    }

    // Same error policy as OpenDotaClient: every transport/format problem becomes DataSourceUnavailableException.
    // The messages never include the URL (it holds the API key).
    private async Task<SteamPlayerSummariesResponse?> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(path, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<SteamPlayerSummariesResponse>(JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new DataSourceUnavailableException($"Steam Web API request failed ({ex.StatusCode?.ToString() ?? "no response"}).");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new DataSourceUnavailableException("Steam Web API returned an invalid response.", ex);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DataSourceUnavailableException("Steam Web API request timed out.");
        }
    }

    // Wrapper so a cached "no profile" (null) can be told apart from "not in the cache".
    private sealed record CachedProfile(PlayerProfile? Profile);
}

// Used when no Steam Web API key is configured: no names or avatars, never an error.
public sealed class NoSteamProfiles : ISteamProfileProvider
{
    public Task<PlayerProfile?> GetProfileAsync(AccountId accountId, CancellationToken cancellationToken) =>
        Task.FromResult<PlayerProfile?>(null);
}
