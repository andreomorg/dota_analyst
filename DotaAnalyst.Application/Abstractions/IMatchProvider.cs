using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Application.Abstractions;

// Where match data comes from (today OpenDota, implemented in Infrastructure).
public interface IMatchProvider
{
    /// <exception cref="DataSourceUnavailableException">The data source could not be reached.</exception>
    Task<IReadOnlyList<PlayerMatch>> GetRecentMatchesAsync(AccountId accountId, CancellationToken cancellationToken);
}
