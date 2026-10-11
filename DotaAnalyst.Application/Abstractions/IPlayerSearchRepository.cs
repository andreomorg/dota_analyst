using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Application.Abstractions;

// Which players were searched and when. Only the (public) account ID is kept, nothing about the visitor.
public interface IPlayerSearchRepository
{
    /// <exception cref="StorageUnavailableException">The database could not be written.</exception>
    Task RecordSearchAsync(AccountId accountId, DateTimeOffset searchedAt, CancellationToken cancellationToken);
}
