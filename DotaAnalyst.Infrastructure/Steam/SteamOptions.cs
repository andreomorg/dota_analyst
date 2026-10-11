namespace DotaAnalyst.Infrastructure.Steam;

// "Steam" section of the configuration. ApiKey is a secret (user-secrets locally, environment variable
// Steam__ApiKey in Docker and on Render); without it, profiles (name and avatar) are simply not shown.
public class SteamOptions
{
    public const string SectionName = "Steam";

    public string? ApiKey { get; init; }

    public Uri BaseUrl { get; init; } = new("https://api.steampowered.com/");

    // Short: the React page waits for /api/v1/me (which shows this profile) before loading anything else.
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}
