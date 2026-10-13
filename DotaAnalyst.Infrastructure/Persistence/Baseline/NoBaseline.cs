using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Infrastructure.Persistence.Baseline;

// Without a database there is no comparison base: analyses just don't show the comparison.
public sealed class NoBaseline : IBaselineRepository
{
    public Task<IReadOnlySet<long>> FindKnownAsync(IReadOnlyCollection<MatchId> matchIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<long>>(new HashSet<long>());

    public Task SaveAsync(BaselineMatch match, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<int> CountCollectedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<IReadOnlyDictionary<int, int>> CountMatchesByHeroAsync(
        BaselineSource source, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<int, int>>(new Dictionary<int, int>());

    public Task<IReadOnlyList<(int HeroId, int Role, BaselineSource Source)>> GetGroupsAsync(
        DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(int, int, BaselineSource)>>([]);

    public Task<IReadOnlyList<BaselineSample>> GetSamplesAsync(
        int heroId, int role, BaselineSource source, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BaselineSample>>([]);
}

// Without a database there is no finished base either: analyses come without the comparison.
public sealed class NoBaselineDistributions : IBaselineDistributionStore
{
    public Task<BaselineDistributionSet?> FindAsync(int heroId, int role, BaselineSource source, CancellationToken cancellationToken) =>
        Task.FromResult<BaselineDistributionSet?>(null);

    public Task ReplaceAllAsync(IReadOnlyList<BaselineDistributionSet> sets, DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}