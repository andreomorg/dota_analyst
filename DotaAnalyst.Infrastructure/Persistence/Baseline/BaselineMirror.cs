using DotaAnalyst.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DotaAnalyst.Infrastructure.Persistence.Baseline;

// The production mirror of the finished comparison base (Neon), from ConnectionStrings:Mirror on the collector's
// machine. It only ever replaces baseline_distributions; the mirror's schema is migrated by the production deploy,
// never from here. Its own StorageGuard: a mirror that is down never makes the local database look down.
public sealed class BaselineMirror : IBaselineMirror
{
    public const string ConnectionStringName = "Mirror";

    private readonly BaselineDistributionStore? store;

    public BaselineMirror(string? connectionString, TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Configure(options, connectionString);
        store = new BaselineDistributionStore(new PooledDbContextFactory<AppDbContext>(options.Options), new StorageGuard(timeProvider));
    }

    public bool IsConfigured => store is not null;

    public Task ReplaceAllAsync(IReadOnlyList<BaselineDistributionSet> sets, DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        store?.ReplaceAllAsync(sets, publishedAt, cancellationToken) ?? Task.CompletedTask;
}
