using DotaAnalyst.Domain.Analysis;

namespace DotaAnalyst.Application.Abstractions;

// The finished comparison base of one hero in one role from one source; OpponentHeroId narrows it to a lane matchup.
// This is what the sites read; the raw matches stay on the collector's machine.
public record BaselineDistributionSet(
    int HeroId, int Role, BaselineSource Source, int Matches, IReadOnlyList<MetricDistribution> Distributions,
    int? OpponentHeroId = null);

// Where the finished base is read from: the site's own database (local on the project owner's machine, Neon in
// production — a mirror of the local one).
public interface IBaselineDistributionStore
{
    /// <returns>Null when the base has nothing for that hero, role and source.</returns>
    /// <exception cref="StorageUnavailableException">The database could not be read.</exception>
    Task<BaselineDistributionSet?> FindAsync(int heroId, int role, BaselineSource source, CancellationToken cancellationToken);

    Task<BaselineDistributionSet?> FindMatchupAsync(
        int heroId, int role, int opponentHeroId, BaselineSource source, CancellationToken cancellationToken) =>
        Task.FromResult<BaselineDistributionSet?>(null);

    // Replaces the whole finished base at once (readers never see half of it).
    /// <exception cref="StorageUnavailableException">The database could not be written.</exception>
    Task ReplaceAllAsync(IReadOnlyList<BaselineDistributionSet> sets, DateTimeOffset publishedAt, CancellationToken cancellationToken);
}

// The production mirror of the finished base (Neon), written only by the collector's machine.
public interface IBaselineMirror
{
    // False when there is no mirror configured (only the local site gets the base).
    bool IsConfigured { get; }

    /// <exception cref="StorageUnavailableException">The mirror could not be written.</exception>
    Task ReplaceAllAsync(IReadOnlyList<BaselineDistributionSet> sets, DateTimeOffset publishedAt, CancellationToken cancellationToken);
}
