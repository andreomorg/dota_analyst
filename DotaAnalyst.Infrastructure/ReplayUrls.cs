namespace DotaAnalyst.Infrastructure;

// Replay addresses as they come from outside (OpenDota's JSON, our saved matches) -> MatchDetails.ReplayUrl.
// Shared by OpenDotaMappings and StoredMatchMappings, so neither depends on the other.
internal static class ReplayUrls
{
    // Only an absolute http(s) address is kept: anything else could not be downloaded anyway, and the address
    // is handed to the replay parser.
    public static Uri? Parse(string? replayUrl) =>
        Uri.TryCreate(replayUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
}
