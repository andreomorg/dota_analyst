using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Application.Abstractions;

// Public Steam profiles (name and avatar), from the Steam Web API.
public interface ISteamProfileProvider
{
    /// <returns>The profile, or null when Steam doesn't know the account or profiles are not configured.</returns>
    /// <exception cref="DataSourceUnavailableException">Steam could not be reached.</exception>
    Task<PlayerProfile?> GetProfileAsync(AccountId accountId, CancellationToken cancellationToken);
}
