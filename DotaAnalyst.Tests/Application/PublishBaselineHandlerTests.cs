using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Application;

public class PublishBaselineHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static BaselineSample Sample(int deaths) =>
        new(new HeroMetrics(31, Team.Radiant, 5, deaths, 0, 0, 0, 0, 2000, null, 10, 0.1, 5, 3, 300, 0, 1), 1800);

    [Fact]
    public async Task Publishes_TheFinishedBase_LocallyAndToTheMirror()
    {
        var local = new Store();
        var mirror = new Mirror(configured: true);
        var raw = new RawBase(new() { [(31, 5, BaselineSource.Pro)] = Enumerable.Range(1, 12).Select(Sample).ToList() });

        var published = await new PublishBaselineHandler(raw, local, mirror, new FixedTimeProvider(Now), NullLogger<PublishBaselineHandler>.Instance)
            .PublishAsync(CancellationToken.None);

        Assert.Equal(1, published);
        var set = Assert.Single(local.Sets!);
        Assert.Equal((31, 5, BaselineSource.Pro, 12), (set.HeroId, set.Role, set.Source, set.Matches));
        Assert.Contains(set.Distributions, distribution => distribution.Metric == ComparedMetric.DeathsPer30);
        Assert.Same(local.Sets, mirror.Sets);
        Assert.Equal(Now - PublishBaselineHandler.Window, raw.Since);
    }

    [Fact]
    public async Task WithoutAMirror_OnlyTheLocalSiteGetsIt()
    {
        var local = new Store();
        var mirror = new Mirror(configured: false);

        await new PublishBaselineHandler(new RawBase([]), local, mirror, new FixedTimeProvider(Now), NullLogger<PublishBaselineHandler>.Instance)
            .PublishAsync(CancellationToken.None);

        Assert.NotNull(local.Sets);
        Assert.Null(mirror.Sets);
    }

    [Fact]
    public async Task PublishesLaneMatchupDistributionsAlongsideGeneralHeroDistributions()
    {
        var local = new Store();
        var matchups = new Dictionary<(int, int, int, BaselineSource), List<BaselineSample>>
        {
            [(31, 5, 44, BaselineSource.Pro)] = Enumerable.Range(1, 12)
                .Select(deaths => Sample(deaths) with { OpponentHeroId = 44 })
                .ToList(),
            // Too few matches to compare anything: not published (the mirror stays small).
            [(31, 5, 77, BaselineSource.Pro)] = Enumerable.Range(1, 3)
                .Select(deaths => Sample(deaths) with { OpponentHeroId = 77 })
                .ToList(),
        };
        var raw = new RawBase([], matchups);

        await new PublishBaselineHandler(raw, local, new Mirror(configured: false),
                new FixedTimeProvider(Now), NullLogger<PublishBaselineHandler>.Instance)
            .PublishAsync(CancellationToken.None);

        var matchup = Assert.Single(local.Sets!);
        Assert.Equal((31, 5, 44, 12),
            (matchup.HeroId, matchup.Role, matchup.OpponentHeroId, matchup.Matches));
        Assert.Contains(matchup.Distributions, distribution => distribution.Metric == ComparedMetric.DeathsPer30);
    }

    [Fact]
    public async Task MirrorFailure_PropagatesAfterLocalPublication_WithoutRollingItBack()
    {
        var local = new Store();
        var failure = new StorageUnavailableException("Mirror unavailable.");
        var mirror = new FailingMirror(failure);
        var raw = new RawBase(new() { [(31, 5, BaselineSource.Pro)] = Enumerable.Range(1, 12).Select(Sample).ToList() });
        var handler = new PublishBaselineHandler(raw, local, mirror, new FixedTimeProvider(Now),
            NullLogger<PublishBaselineHandler>.Instance);

        // Current behavior: local publication completes first; a mirror failure escapes to the caller.
        var thrown = await Assert.ThrowsAsync<StorageUnavailableException>(() => handler.PublishAsync(CancellationToken.None));

        Assert.Same(failure, thrown);
        var published = Assert.Single(local.Sets!);
        Assert.Equal(12, published.Matches);
        Assert.Same(local.Sets, mirror.Sets);
    }

    private sealed class FailingMirror(StorageUnavailableException failure) : IBaselineMirror
    {
        public bool IsConfigured => true;
        public IReadOnlyList<BaselineDistributionSet>? Sets { get; private set; }

        public Task ReplaceAllAsync(IReadOnlyList<BaselineDistributionSet> sets, DateTimeOffset publishedAt,
            CancellationToken cancellationToken)
        {
            Sets = sets;
            throw failure;
        }
    }

    private sealed class RawBase(
        Dictionary<(int, int, BaselineSource), List<BaselineSample>> groups,
        Dictionary<(int, int, int, BaselineSource), List<BaselineSample>>? matchups = null) : IBaselineRepository
    {
        private readonly Dictionary<(int, int, int, BaselineSource), List<BaselineSample>> _matchups = matchups ?? [];

        public DateTimeOffset Since { get; private set; }

        public Task<IReadOnlySet<long>> FindKnownAsync(IReadOnlyCollection<MatchId> matchIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>>(new HashSet<long>());

        public Task SaveAsync(BaselineMatch match, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> CountCollectedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IReadOnlyDictionary<int, int>> CountMatchesByHeroAsync(
            BaselineSource source, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, int>>(new Dictionary<int, int>());

        public Task<IReadOnlyList<(int HeroId, int Role, BaselineSource Source)>> GetGroupsAsync(DateTimeOffset since, CancellationToken cancellationToken)
        {
            Since = since;
            return Task.FromResult<IReadOnlyList<(int, int, BaselineSource)>>(groups.Keys.ToList());
        }

        public Task<IReadOnlyList<BaselineSample>> GetSamplesAsync(
            int heroId, int role, BaselineSource source, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BaselineSample>>(groups[(heroId, role, source)]);

        public Task<IReadOnlyList<(int HeroId, int Role, int OpponentHeroId, BaselineSource Source)>> GetMatchupGroupsAsync(
            DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(int, int, int, BaselineSource)>>(_matchups.Keys.ToList());

        public Task<IReadOnlyList<BaselineSample>> GetMatchupSamplesAsync(
            int heroId, int role, int opponentHeroId, BaselineSource source, DateTimeOffset since,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BaselineSample>>(_matchups[(heroId, role, opponentHeroId, source)]);
    }

    private sealed class Store : IBaselineDistributionStore
    {
        public IReadOnlyList<BaselineDistributionSet>? Sets { get; private set; }

        public Task<BaselineDistributionSet?> FindAsync(int heroId, int role, BaselineSource source, CancellationToken cancellationToken) =>
            Task.FromResult<BaselineDistributionSet?>(null);

        public Task ReplaceAllAsync(IReadOnlyList<BaselineDistributionSet> sets, DateTimeOffset publishedAt, CancellationToken cancellationToken)
        {
            Sets = sets;
            return Task.CompletedTask;
        }
    }

    private sealed class Mirror(bool configured) : IBaselineMirror
    {
        public IReadOnlyList<BaselineDistributionSet>? Sets { get; private set; }

        public bool IsConfigured => configured;

        public Task ReplaceAllAsync(IReadOnlyList<BaselineDistributionSet> sets, DateTimeOffset publishedAt, CancellationToken cancellationToken)
        {
            Sets = sets;
            return Task.CompletedTask;
        }
    }
}
