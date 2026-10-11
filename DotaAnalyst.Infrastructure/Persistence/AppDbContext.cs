using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using DotaAnalyst.Infrastructure.Persistence.Matches;
using DotaAnalyst.Infrastructure.Persistence.Players;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DotaAnalyst.Infrastructure.Persistence;

// The PostgreSQL database. Its tables hold "records" (classes made for storage), never domain entities:
// the domain model knows nothing about EF Core, and each repository converts between the two.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<MatchRecord> Matches => Set<MatchRecord>();

    public DbSet<PlayerSearchRecord> PlayerSearches => Set<PlayerSearchRecord>();

    public DbSet<CatalogEntryRecord> CatalogEntries => Set<CatalogEntryRecord>();

    // ASP.NET Core Data Protection keys (they encrypt the future login cookie), so they survive restarts.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Shared by the app, the dotnet-ef tool and the tests, so all of them talk to the database the same way.
    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(
                // A serverless database (Neon) may need a moment to wake up: retry, but few times and briefly,
                // so a database that is really down doesn't hold requests for long (see also StorageGuard).
                maxRetryCount: 2,
                maxRetryDelay: TimeSpan.FromSeconds(2),
                errorCodesToAdd: null))
            // Tables and columns as PostgreSQL likes them: PlayerSearches -> player_searches.
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        // Picks up every IEntityTypeConfiguration in this project (MatchRecordConfiguration, ...).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
