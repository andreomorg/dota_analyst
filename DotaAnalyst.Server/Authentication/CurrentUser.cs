using System.Security.Claims;
using System.Text.RegularExpressions;
using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Server.Authentication;

// The one place that knows how the logged-in user is stored in the session cookie (claims) and how
// Steam identifies a user in the OpenID answer. Everything else just asks for an AccountId.
public static partial class CurrentUser
{
    // Our own claim: the Dota account ID of the logged-in user.
    public const string AccountIdClaim = "account_id";

    // Steam's OpenID answer identifies the user as https://steamcommunity.com/openid/id/<SteamID64>.
    [GeneratedRegex(@"^https://steamcommunity\.com/openid/id/(\d{17})$")]
    private static partial Regex SteamIdentifier();

    /// <returns>False when the identifier is not a Steam account (then the login is refused).</returns>
    public static bool TryParseSteamIdentifier(string? identifier, out AccountId accountId)
    {
        accountId = default;
        var match = SteamIdentifier().Match(identifier ?? "");
        // Must really be a SteamID64: a small number would be read as an account ID by TryCreate.
        return match.Success
            && long.TryParse(match.Groups[1].Value, out var steamId64)
            && steamId64 > AccountId.SteamId64Offset
            && AccountId.TryCreate(steamId64, out accountId);
    }

    // What goes into the session cookie: only the account ID (the SteamID64 can be computed back from it).
    public static ClaimsPrincipal CreatePrincipal(AccountId accountId, string authenticationType) =>
        new(new ClaimsIdentity([new Claim(AccountIdClaim, accountId.Value.ToString())], authenticationType));

    public static bool TryGetAccountId(ClaimsPrincipal user, out AccountId accountId)
    {
        accountId = default;
        return user.Identity?.IsAuthenticated == true
            && long.TryParse(user.FindFirstValue(AccountIdClaim), out var value)
            && AccountId.TryCreate(value, out accountId);
    }
}
