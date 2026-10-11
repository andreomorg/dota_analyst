namespace DotaAnalyst.Infrastructure.Steam;

// Only the fields we use from GET ISteamUser/GetPlayerSummaries/v2:
// { "response": { "players": [ { "steamid": "...", "personaname": "...", "avatarfull": "https://..." } ] } }
public record SteamPlayerSummariesResponse(SteamPlayerSummaries? Response);

public record SteamPlayerSummaries(IReadOnlyList<SteamPlayerSummary>? Players);

public record SteamPlayerSummary(string? Steamid, string? Personaname, string? Avatarfull);
