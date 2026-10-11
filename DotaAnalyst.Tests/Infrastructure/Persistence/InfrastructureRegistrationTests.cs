using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure;
using DotaAnalyst.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

// AddInfrastructure wired to a real database: the registrations resolve, and Data Protection keys
// end up in the database (so they survive a restart, which is what the future login needs).
[Collection(PostgresCollection.Name)]
public class InfrastructureRegistrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddInfrastructure_WithoutConnectionString_RunsWithoutDatabase()
    {
        await using var services = BuildServices(connectionString: null);
        await using var scope = services.CreateAsyncScope();

        Assert.IsType<NoDatabase>(scope.ServiceProvider.GetRequiredService<IMatchRepository>());
        Assert.Null(scope.ServiceProvider.GetService<IDbContextFactory<AppDbContext>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IHeroCatalog>());
    }

    [Fact]
    public async Task AddInfrastructure_ResolvesRepositoriesAndScopedDbContext()
    {
        await using var services = BuildServices();
        await using var scope = services.CreateAsyncScope();

        Assert.IsNotType<NoDatabase>(scope.ServiceProvider.GetRequiredService<IMatchRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPlayerSearchRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IHeroCatalog>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task DataProtectionKeys_AreSharedThroughTheDatabase()
    {
        // Two separate apps (like the container before and after a deploy) pointing to the same database.
        string protectedText;
        await using (var before = BuildServices())
        {
            protectedText = before.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("test").Protect("hello");
        }

        await using var after = BuildServices();
        var text = after.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedText);

        Assert.Equal("hello", text);
        await using var db = await postgres.DbContextFactory.CreateDbContextAsync();
        Assert.NotEmpty(await db.DataProtectionKeys.ToListAsync());
    }

    private ServiceProvider BuildServices() => BuildServices(postgres.ConnectionString);

    private static ServiceProvider BuildServices(string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = connectionString,
                ["OpenDota:BaseUrl"] = "https://api.opendota.com/api/",
                ["OpenDota:Timeout"] = "00:00:10",
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
