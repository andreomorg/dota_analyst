using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Abstractions;

// A match as saved in our database, and when it was saved (or last refreshed).
public record SavedMatch(MatchDetails Match, DateTimeOffset SavedAt);

// Matches saved in our own database, so they don't have to be fetched from OpenDota again.
public interface IMatchRepository
{
    /// <returns>The saved match, or null when it was never saved.</returns>
    /// <exception cref="StorageUnavailableException">The database could not be read.</exception>
    Task<SavedMatch?> FindAsync(MatchId matchId, CancellationToken cancellationToken);

    // Saves a new match or refreshes a saved one. A saved parsed match is never replaced by an unparsed one,
    // and saving the same match twice at once is not an error. savedAt comes from the app's clock (the same
    // one that later measures how old the saved copy is).
    /// <exception cref="StorageUnavailableException">The database could not be written.</exception>
    Task SaveAsync(MatchDetails match, DateTimeOffset savedAt, CancellationToken cancellationToken);
}
