using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Persistence.Replays;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.Persistence.Baseline;

// The comparison base in PostgreSQL. Same rules as the other repositories: one short-lived DbContext per operation,
// everything through StorageGuard.
public class BaselineRepository(
    IDbContextFactory<AppDbContext> dbContextFactory,
    StorageGuard storageGuard,
    ILogger<BaselineRepository> logger) : IBaselineRepository
{

    public Task<IReadOnlySet<long>> FindKnownAsync(IReadOnlyCollection<MatchId> matchIds, CancellationToken cancellationToken) =>
        storageGuard.RunAsync("reading the comparison base's matches", async () =>
        {
            var ids = matchIds.Select(id => id.Value).ToList();
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var known = await db.BaselineMatches
                .AsNoTracking()
                .Where(match => ids.Contains(match.MatchId))
                .Select(match => match.MatchId)
                .ToListAsync(cancellationToken);
            return (IReadOnlySet<long>)known.ToHashSet();
        });

    public Task SaveAsync(BaselineMatch match, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"saving match {match.MatchId} to the comparison base", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var source = SourceName(match.Source);
            var start = match.StartTime.ToUniversalTime();

            // All or nothing, inside the execution strategy (retries repeat the whole transaction).
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                // Tried again (a collector restarted mid-match): the newer try replaces the older.
                await db.BaselineMetrics.Where(row => row.MatchId == match.MatchId.Value).ExecuteDeleteAsync(cancellationToken);
                await db.BaselineMatches.Where(row => row.MatchId == match.MatchId.Value).ExecuteDeleteAsync(cancellationToken);

                db.BaselineMatches.Add(new BaselineMatchRecord
                {
                    MatchId = match.MatchId.Value,
                    Source = source,
                    Status = StatusName(match.Status),
                    StartTime = start,
                    DurationSeconds = match.DurationSeconds,
                    TriedAt = match.TriedAt.ToUniversalTime(),
                });
                db.BaselineMetrics.AddRange(match.Metrics.Select(hero => new BaselineMetricsRecord
                {
                    MatchId = match.MatchId.Value,
                    HeroId = hero.HeroId,
                    Role = hero.Role,
                    OpponentHeroId = LaneMatchup.OpposingRole(hero.Role) is { } opposingRole
                        ? match.Metrics.FirstOrDefault(other => other.Team != hero.Team && other.Role == opposingRole)?.HeroId
                        : null,
                    Source = source,
                    StartTime = start,
                    DurationSeconds = match.DurationSeconds,
                    Metrics = JsonSerializer.Serialize(StoredReplayMappings.ToStored(hero), StoredReplayMappings.JsonOptions),
                }));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        });

    public Task<int> CountCollectedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        storageGuard.RunAsync("counting the comparison base's matches", async () =>
        {
            var from = since.ToUniversalTime();
            var collected = StatusName(BaselineMatchStatus.Collected);
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await db.BaselineMatches.CountAsync(match => match.Status == collected && match.TriedAt >= from, cancellationToken);
        });

    public Task<IReadOnlyDictionary<int, int>> CountMatchesByHeroAsync(
        BaselineSource source, DateTimeOffset since, CancellationToken cancellationToken) =>
        storageGuard.RunAsync("counting the comparison base's matches per hero", async () =>
        {
            var name = SourceName(source);
            var from = since.ToUniversalTime();
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return (IReadOnlyDictionary<int, int>)await db.BaselineMetrics
                .AsNoTracking()
                .Where(row => row.Source == name && row.StartTime >= from)
                .GroupBy(row => row.HeroId)
                .Select(group => new { HeroId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(group => group.HeroId, group => group.Count, cancellationToken);
        });

    public Task<IReadOnlyList<(int HeroId, int Role, BaselineSource Source)>> GetGroupsAsync(
        DateTimeOffset since, CancellationToken cancellationToken) =>
        storageGuard.RunAsync("listing the comparison base's heroes", async () =>
        {
            var from = since.ToUniversalTime();
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var groups = await db.BaselineMetrics
                .AsNoTracking()
                .Where(row => row.StartTime >= from)
                .Select(row => new { row.HeroId, row.Role, row.Source })
                .Distinct()
                .ToListAsync(cancellationToken);
            return (IReadOnlyList<(int, int, BaselineSource)>)groups
                .Select(group => (group.HeroId, group.Role, group.Source == SourceName(BaselineSource.Pro) ? BaselineSource.Pro : BaselineSource.HighRank))
                .ToList();
        });

    public Task<IReadOnlyList<(int HeroId, int Role, int OpponentHeroId, BaselineSource Source)>> GetMatchupGroupsAsync(
        DateTimeOffset since, CancellationToken cancellationToken) =>
        storageGuard.RunAsync("listing the comparison base's lane matchups", async () =>
        {
            var from = since.ToUniversalTime();
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var groups = await db.BaselineMetrics
                .AsNoTracking()
                .Where(row => row.StartTime >= from && row.OpponentHeroId != null)
                .Select(row => new { row.HeroId, row.Role, OpponentHeroId = row.OpponentHeroId!.Value, row.Source })
                .Distinct()
                .ToListAsync(cancellationToken);
            return (IReadOnlyList<(int, int, int, BaselineSource)>)groups
                .Select(group => (group.HeroId, group.Role, group.OpponentHeroId,
                    group.Source == SourceName(BaselineSource.Pro) ? BaselineSource.Pro : BaselineSource.HighRank))
                .ToList();
        });

    public Task<IReadOnlyList<BaselineSample>> GetSamplesAsync(
        int heroId, int role, BaselineSource source, DateTimeOffset since, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"reading the comparison base of hero {heroId}", async () =>
        {
            var name = SourceName(source);
            var from = since.ToUniversalTime();
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await db.BaselineMetrics
                .AsNoTracking()
                .Where(row => row.HeroId == heroId && row.Role == role && row.Source == name && row.StartTime >= from)
                .Select(row => new { row.Metrics, row.DurationSeconds })
                .ToListAsync(cancellationToken);

            var samples = new List<BaselineSample>();
            foreach (var row in rows)
            {
                try
                {
                    var stored = JsonSerializer.Deserialize<StoredHeroMetrics>(row.Metrics, StoredReplayMappings.JsonOptions)
                        ?? throw new InvalidDataException("Empty metrics.");
                    samples.Add(new BaselineSample(StoredReplayMappings.ToDomain(stored), row.DurationSeconds));
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException)
                {
                    // One unreadable row only leaves the base a little smaller.
                    logger.LogWarning(ex, "A comparison base row of hero {HeroId} can't be read", heroId);
                }
            }

            return (IReadOnlyList<BaselineSample>)samples;
        });

    public Task<IReadOnlyList<BaselineSample>> GetMatchupSamplesAsync(
        int heroId, int role, int opponentHeroId, BaselineSource source, DateTimeOffset since,
        CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"reading the lane matchup base of hero {heroId}", async () =>
        {
            var name = SourceName(source);
            var from = since.ToUniversalTime();
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await db.BaselineMetrics
                .AsNoTracking()
                .Where(row => row.HeroId == heroId && row.Role == role && row.OpponentHeroId == opponentHeroId
                    && row.Source == name && row.StartTime >= from)
                .Select(row => new { row.Metrics, row.DurationSeconds })
                .ToListAsync(cancellationToken);

            var samples = new List<BaselineSample>();
            foreach (var row in rows)
            {
                try
                {
                    var stored = JsonSerializer.Deserialize<StoredHeroMetrics>(row.Metrics, StoredReplayMappings.JsonOptions)
                        ?? throw new InvalidDataException("Empty metrics.");
                    samples.Add(new BaselineSample(StoredReplayMappings.ToDomain(stored), row.DurationSeconds, opponentHeroId));
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException)
                {
                    logger.LogWarning(ex, "A lane matchup base row of hero {HeroId} can't be read", heroId);
                }
            }

            return (IReadOnlyList<BaselineSample>)samples;
        });

    internal static string SourceName(BaselineSource source) => source switch
    {
        BaselineSource.Pro => "pro",
        _ => "highRank",
    };

    private static string StatusName(BaselineMatchStatus status) => status switch
    {
        BaselineMatchStatus.Collected => "collected",
        BaselineMatchStatus.NoReplay => "noReplay",
        _ => "failed",
    };
}
