using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.Persistence;

// Brings the database schema up to date (applies pending migrations) when the app starts.
// - The first attempt runs before the app accepts requests, and before Data Protection reads its keys table
//   (hosted services start in registration order, and this one is registered first).
// - If the database can't be reached (e.g. Neon waking up), it keeps trying every RetryEvery in the background,
//   so a database that was down at boot doesn't stay without tables until the next restart. Meanwhile the app
//   works on OpenDota alone.
// Fine with a single instance (our case); with several instances, migrations would be a deploy step instead.
public sealed class DatabaseMigrator(
    IServiceProvider services,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseMigrator> logger) : BackgroundService
{
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(30);

    private bool done;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        done = await TryMigrateAsync(cancellationToken);
        // Starts ExecuteAsync (the background retries) without waiting for it.
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!done && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(RetryEvery, stoppingToken);
            done = await TryMigrateAsync(stoppingToken);
        }
    }

    // True when there is nothing more to do: migrated, migrations turned off, or no database at all.
    private async Task<bool> TryMigrateAsync(CancellationToken cancellationToken)
    {
        // Registered only when a connection string exists (see AddInfrastructure).
        var dbContextFactory = services.GetService<IDbContextFactory<AppDbContext>>();
        if (dbContextFactory is null)
        {
            logger.LogWarning("No database configured (ConnectionStrings:Database): running on OpenDota only");
            return true;
        }

        if (!options.Value.MigrateOnStartup)
        {
            logger.LogInformation("Database migrations on startup are turned off");
            return true;
        }

        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database schema is up to date");
            return true;
        }
        catch (Exception ex) when (StorageGuard.IsStorageFailure(ex))
        {
            logger.LogError(ex, "Could not apply database migrations; trying again in {RetryEvery}", RetryEvery);
            return false;
        }
    }
}
