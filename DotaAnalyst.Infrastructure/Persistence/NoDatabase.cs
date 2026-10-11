using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;

namespace DotaAnalyst.Infrastructure.Persistence;

// Used when no connection string is configured (e.g. "docker run" without a database, or a Render
// service whose secret wasn't filled in yet): nothing is ever saved or found, and the app runs on
// OpenDota alone, exactly as before the database existed.
public sealed class NoDatabase : IMatchRepository, IPlayerSearchRepository, ICatalogStore
{
    public Task<SavedMatch?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
        Task.FromResult<SavedMatch?>(null);

    public Task SaveAsync(MatchDetails match, DateTimeOffset savedAt, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task RecordSearchAsync(AccountId accountId, DateTimeOffset searchedAt, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<SavedCatalog?> LoadAsync(string catalog, CancellationToken cancellationToken) =>
        Task.FromResult<SavedCatalog?>(null);

    public Task ReplaceAsync(
        string catalog, IReadOnlyDictionary<int, string> names, DateTimeOffset updatedAt, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
