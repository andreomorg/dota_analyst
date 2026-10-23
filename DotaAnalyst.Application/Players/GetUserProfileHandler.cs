using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Players;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Players;

// Use case: who is the logged-in user (IDs, plus Steam name and avatar when available).
public class GetUserProfileHandler(ISteamProfileProvider steamProfiles, ILogger<GetUserProfileHandler> logger)
{
    public async Task<UserProfile> HandleAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        PlayerProfile? profile = null;
        try
        {
            profile = await steamProfiles.GetProfileAsync(accountId, cancellationToken);
        }
        catch (DataSourceUnavailableException ex)
        {
            // The name and avatar are a nice-to-have: the user is still logged in without them.
            logger.LogWarning(ex, "Could not load the Steam profile of account {AccountId}", accountId);
        }

        return new UserProfile(
            AccountId: accountId.Value,
            SteamId64: accountId.SteamId64.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PersonaName: profile?.PersonaName,
            AvatarUrl: profile?.AvatarUrl);
    }
}
