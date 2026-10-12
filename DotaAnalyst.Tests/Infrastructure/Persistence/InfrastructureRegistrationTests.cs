using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure;
using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Replays;
using DotaAnalyst.Infrastructure.ReplayParser;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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

    [Fact]
    public async Task ReplayParsing_WithParserAddressAndDatabase_IsOn()
    {
        await using var services = BuildServices(postgres.ConnectionString, ParserUrl);
        await using var scope = services.CreateAsyncScope();

        Assert.IsType<ReplayParserClient>(scope.ServiceProvider.GetRequiredService<IReplayParser>());
        Assert.IsType<ReplayParseQueue>(scope.ServiceProvider.GetRequiredService<IReplayParseQueue>());
        Assert.IsType<ReplayAnalysisRepository>(scope.ServiceProvider.GetRequiredService<IReplayAnalysisRepository>());
        Assert.Contains(services.GetServices<IHostedService>(), service => service is ReplayParseWorker);
    }

    [Fact]
    public async Task ReplayParsing_WithoutParserAddress_IsOff_ButSavedResultsAreStillRead()
    {
        await using var services = BuildServices(postgres.ConnectionString, replayParserUrl: " ");
        await using var scope = services.CreateAsyncScope();

        Assert.IsType<NoReplayParser>(scope.ServiceProvider.GetRequiredService<IReplayParser>());
        Assert.IsType<DisabledReplayParseQueue>(scope.ServiceProvider.GetRequiredService<IReplayParseQueue>());
        Assert.IsType<ReplayAnalysisRepository>(scope.ServiceProvider.GetRequiredService<IReplayAnalysisRepository>());
        Assert.DoesNotContain(services.GetServices<IHostedService>(), service => service is ReplayParseWorker);
    }

    [Fact]
    public async Task ReplayParsing_WithoutDatabase_IsOff()
    {
        // Results could never be kept: the same replay would be parsed on every visit.
        await using var services = BuildServices(connectionString: null, ParserUrl);
        await using var scope = services.CreateAsyncScope();

        Assert.IsType<NoReplayParser>(scope.ServiceProvider.GetRequiredService<IReplayParser>());
        Assert.IsType<DisabledReplayParseQueue>(scope.ServiceProvider.GetRequiredService<IReplayParseQueue>());
        Assert.IsType<NoReplayAnalyses>(scope.ServiceProvider.GetRequiredService<IReplayAnalysisRepository>());
        Assert.DoesNotContain(services.GetServices<IHostedService>(), service => service is ReplayParseWorker);
    }

    [Theory]
    [InlineData("replay-parser:8081")] // not absolute
    [InlineData("ftp://replay-parser:8081/")]
    [InlineData("https://example.ngrok-free.app/parser")] // no trailing '/': "v1/laning" would replace "parser"
    public async Task ReplayParsing_InvalidParserAddress_IsOffWithAWarning_AndTheAppStillStarts(string replayParserUrl)
    {
        await using var services = BuildServices(postgres.ConnectionString, replayParserUrl);
        await using var scope = services.CreateAsyncScope();

        // ValidateOnStart: what the host runs before starting; it must not stop the app.
        services.GetRequiredService<IStartupValidator>().Validate();
        Assert.IsType<NoReplayParser>(scope.ServiceProvider.GetRequiredService<IReplayParser>());
        Assert.IsType<DisabledReplayParseQueue>(scope.ServiceProvider.GetRequiredService<IReplayParseQueue>());
        var warning = Assert.Single(StartupWarnings(services));
        Assert.Contains("ReplayParser:BaseUrl", warning);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(" ", true)]
    [InlineData("secret-key", false)]
    public async Task ReplayParsing_OnWithoutApiKey_WarnsAtStartup(string? apiKey, bool warns)
    {
        await using var services = BuildServices(postgres.ConnectionString, ParserUrl, apiKey);

        services.GetRequiredService<IStartupValidator>().Validate();
        Assert.Equal(warns, StartupWarnings(services).Any(warning => warning.Contains("ReplayParser:ApiKey")));
        Assert.All(StartupWarnings(services), warning => Assert.DoesNotContain("secret-key", warning));
    }

    private static List<string> StartupWarnings(ServiceProvider services) =>
        services.GetServices<IHostedService>().OfType<ReplayParsingStartupWarning>().Select(notice => notice.Message).ToList();

    private const string ParserUrl = "http://replay-parser:8081/";

    private ServiceProvider BuildServices() => BuildServices(postgres.ConnectionString);

    private static ServiceProvider BuildServices(
        string? connectionString, string? replayParserUrl = null, string? replayParserApiKey = "test-key")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = connectionString,
                ["OpenDota:BaseUrl"] = "https://api.opendota.com/api/",
                ["OpenDota:Timeout"] = "00:00:10",
                ["ReplayParser:BaseUrl"] = replayParserUrl,
                ["ReplayParser:ApiKey"] = replayParserApiKey,
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
