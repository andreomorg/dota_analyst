using Microsoft.EntityFrameworkCore;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public class MigrationsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_AreAllApplied()
    {
        await using var db = await postgres.DbContextFactory.CreateDbContextAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Model_HasNoChangesMissingAMigration()
    {
        // Fails when an entity/configuration changed but nobody ran "dotnet ef migrations add".
        await using var db = await postgres.DbContextFactory.CreateDbContextAsync();

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
