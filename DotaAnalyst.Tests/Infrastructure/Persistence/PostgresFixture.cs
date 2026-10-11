using DotaAnalyst.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

// A real PostgreSQL in a throwaway Docker container (Testcontainers), started once for every test class
// in the "Postgres" collection and removed at the end. Needs Docker running; the first run downloads the image.
public sealed class PostgresFixture : IAsyncLifetime
{
    // Same major version as docker-compose.yml.
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => container.GetConnectionString();

    public IDbContextFactory<AppDbContext> DbContextFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        DbContextFactory = new TestDbContextFactory(ConnectionString);

        // The real migrations, exactly as the app applies them on startup.
        await using var db = await DbContextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    // Each test starts with empty tables (tests in a collection run one after another, never at the same time).
    public async Task ResetAsync()
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE matches, player_searches, catalog_entries, data_protection_keys");
    }

    private sealed class TestDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>();
            AppDbContext.Configure(options, connectionString);
            return new AppDbContext(options.Options);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
