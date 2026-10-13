using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Abstractions;

// What happened to a match the collector tried.
public enum BaselineMatchStatus
{
    // Parsed: its heroes' metrics are in the base.
    Collected,
    // No replay to read (OpenDota has no replay address, or Valve no longer has the file).
    NoReplay,
    // The replay couldn't be read.
    Failed,
}

// A match the collector tried, with the metrics of its heroes when it was collected.
public record BaselineMatch(
    MatchId MatchId,
    BaselineSource Source,
    DateTimeOffset StartTime,
    int DurationSeconds,
    BaselineMatchStatus Status,
    IReadOnlyList<HeroMetrics> Metrics,
    DateTimeOffset TriedAt);

// The raw comparison base: metrics of other players' matches (pro and high-rank), collected in the background on the
// project owner's machine (local database only; production gets the finished base, IBaselineDistributionStore).
public interface IBaselineRepository
{
    /// <exception cref="StorageUnavailableException">The database could not be read.</exception>
    Task<IReadOnlySet<long>> FindKnownAsync(IReadOnlyCollection<MatchId> matchIds, CancellationToken cancellationToken);

    /// <exception cref="StorageUnavailableException">The database could not be written.</exception>
    Task SaveAsync(BaselineMatch match, CancellationToken cancellationToken);

    // How many matches were collected (status Collected) since then.
    /// <exception cref="StorageUnavailableException">The database could not be read.</exception>
    Task<int> CountCollectedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    // How many matches of each hero the base has from that source, in matches that started since then.
    /// <exception cref="StorageUnavailableException">The database could not be read.</exception>
    Task<IReadOnlyDictionary<int, int>> CountMatchesByHeroAsync(
        BaselineSource source, DateTimeOffset since, CancellationToken cancellationToken);

    // Every hero/role/source the base has matches of since then.
    /// <exception cref="StorageUnavailableException">The database could not be read.</exception>
    Task<IReadOnlyList<(int HeroId, int Role, BaselineSource Source)>> GetGroupsAsync(DateTimeOffset since, CancellationToken cancellationToken);

    // The metrics of a hero in a role, in matches that started since then.
    /// <exception cref="StorageUnavailableException">The database could not be read.</exception>
    Task<IReadOnlyList<BaselineSample>> GetSamplesAsync(
        int heroId, int role, BaselineSource source, DateTimeOffset since, CancellationToken cancellationToken);
}
