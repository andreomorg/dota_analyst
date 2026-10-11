namespace DotaAnalyst.Application.Players;

// The logged-in user, as the API returns it (GET /api/v1/me).
public record UserProfile(
    long AccountId,
    // Text, not a number: a SteamID64 is larger than JavaScript can hold exactly (2^53).
    string SteamId64,
    // Null when the Steam profile is unavailable (no Steam Web API key, or Steam is down).
    string? PersonaName,
    Uri? AvatarUrl);
