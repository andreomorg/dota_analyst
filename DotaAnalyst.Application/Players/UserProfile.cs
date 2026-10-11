namespace DotaAnalyst.Application.Players;

/// <summary>The logged-in user.</summary>
/// <param name="AccountId">Dota account ID.</param>
/// <param name="SteamId64">SteamID64, as text: it is larger than JavaScript numbers can hold exactly (2^53).</param>
/// <param name="PersonaName">Steam name; null when the Steam profile is unavailable (no Steam Web API key, or Steam is down).</param>
/// <param name="AvatarUrl">Steam avatar (https); null when unavailable.</param>
public record UserProfile(
    long AccountId,
    string SteamId64,
    string? PersonaName,
    Uri? AvatarUrl);
