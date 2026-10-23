using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Stats;
using DotaAnalyst.Infrastructure.Persistence.Replays;
using Microsoft.EntityFrameworkCore;

namespace DotaAnalyst.Infrastructure.Persistence.Baseline;

// The finished comparison base in a database: the site's own (registered as IBaselineDistributionStore) or the Neon
// mirror (wrapped by BaselineMirror with its own DbContext factory).
public class BaselineDistributionStore(IDbContextFactory<AppDbContext> dbContextFactory, StorageGuard storageGuard)
    : IBaselineDistributionStore
{
    private sealed record StoredDistribution(ComparedMetric Metric, IReadOnlyList<double> Quantiles, int SampleSize);

    private sealed record StoredTimedDistribution(StatKey Stat, int Second, IReadOnlyList<double> Quantiles, int SampleSize);

    // Two decimals are plenty for a comparison and keep the mirror small (percentiles interpolate: 1234.5666...).
    private static List<double> Rounded(IReadOnlyList<double> quantiles) =>
        quantiles.Select(value => Math.Round(value, 2)).ToList();

    public Task<BaselineDistributionSet?> FindAsync(int heroId, int role, BaselineSource source, CancellationToken cancellationToken) =>
        FindSetAsync(heroId, role, null, source, cancellationToken);

    public Task<BaselineDistributionSet?> FindMatchupAsync(
        int heroId, int role, int opponentHeroId, BaselineSource source, CancellationToken cancellationToken) =>
        FindSetAsync(heroId, role, opponentHeroId, source, cancellationToken);

    private Task<BaselineDistributionSet?> FindSetAsync(
        int heroId, int role, int? opponentHeroId, BaselineSource source, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"reading the comparison base of hero {heroId}", async () =>
        {
            var name = BaselineRepository.SourceName(source);
            var storedOpponentHeroId = opponentHeroId ?? 0;
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.BaselineDistributions
                .AsNoTracking()
                .SingleOrDefaultAsync(set => set.HeroId == heroId && set.Role == role
                    && set.OpponentHeroId == storedOpponentHeroId && set.Source == name, cancellationToken);
            if (row is null)
            {
                return null;
            }

            try
            {
                var distributions = JsonSerializer.Deserialize<List<StoredDistribution>>(row.Distributions, StoredReplayMappings.JsonOptions) ?? [];
                var timed = JsonSerializer.Deserialize<List<StoredTimedDistribution>>(row.TimedDistributions, StoredReplayMappings.JsonOptions) ?? [];
                return new BaselineDistributionSet(
                    heroId, role, source, row.Matches,
                    distributions.Select(stored => new MetricDistribution(stored.Metric, stored.Quantiles, stored.SampleSize)).ToList(),
                    opponentHeroId,
                    timed.Select(stored => new TimedDistribution(stored.Stat, stored.Second, stored.Quantiles, stored.SampleSize)).ToList());
            }
            catch (JsonException)
            {
                // An unreadable row (format changed) is as if the hero had no base yet; the next publish replaces it.
                return null;
            }
        });

    public Task ReplaceAllAsync(IReadOnlyList<BaselineDistributionSet> sets, DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        storageGuard.RunAsync("publishing the comparison base", async () =>
        {
            var at = publishedAt.ToUniversalTime();
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                // All or nothing: readers see the old base or the new one, never half of it.
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.BaselineDistributions.ExecuteDeleteAsync(cancellationToken);
                db.BaselineDistributions.AddRange(sets.Select(set => new BaselineDistributionRecord
                {
                    HeroId = set.HeroId,
                    Role = set.Role,
                    OpponentHeroId = set.OpponentHeroId ?? 0,
                    Source = BaselineRepository.SourceName(set.Source),
                    Matches = set.Matches,
                    Distributions = JsonSerializer.Serialize(
                        set.Distributions.Select(d => new StoredDistribution(d.Metric, Rounded(d.Quantiles), d.SampleSize)).ToList(),
                        StoredReplayMappings.JsonOptions),
                    TimedDistributions = JsonSerializer.Serialize(
                        (set.Timed ?? []).Select(d => new StoredTimedDistribution(d.Stat, d.Second, Rounded(d.Quantiles), d.SampleSize)).ToList(),
                        StoredReplayMappings.JsonOptions),
                    PublishedAt = at,
                }));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        });
}
