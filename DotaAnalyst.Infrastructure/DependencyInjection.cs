using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure.Caching;
using DotaAnalyst.Infrastructure.Catalogs;
using DotaAnalyst.Infrastructure.OpenDota;
using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using DotaAnalyst.Infrastructure.Persistence.Matches;
using DotaAnalyst.Infrastructure.Persistence.Players;
using DotaAnalyst.Infrastructure.Steam;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache(options => options.SizeLimit = CacheEntries.SizeLimit);
        services.TryAddSingleton(TimeProvider.System);

        AddOpenDota(services, configuration);
        AddSteamProfiles(services, configuration);
        AddDatabase(services, configuration);

        return services;
    }

    private static void AddSteamProfiles(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SteamOptions.SectionName);
        services.AddOptions<SteamOptions>()
            .Bind(section)
            .Validate(
                options => options.BaseUrl is { IsAbsoluteUri: true } && options.BaseUrl.AbsolutePath.EndsWith('/'),
                "Steam:BaseUrl must be an absolute URL ending with '/'.")
            .Validate(options => options.Timeout > TimeSpan.Zero, "Steam:Timeout must be positive.")
            .ValidateOnStart();

        // Names and avatars are optional: without a Steam Web API key the site just doesn't show them.
        if (string.IsNullOrWhiteSpace(section.GetValue<string>(nameof(SteamOptions.ApiKey))))
        {
            services.AddSingleton<ISteamProfileProvider, NoSteamProfiles>();
            return;
        }

        services.AddHttpClient<ISteamProfileProvider, SteamProfileProvider>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<SteamOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.Timeout = options.Timeout;
            })
            // The default HttpClient logs write the full request URL, and Steam's API key travels in it.
            .RemoveAllLoggers();
    }

    private static void AddOpenDota(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OpenDotaOptions>()
            .Bind(configuration.GetSection(OpenDotaOptions.SectionName))
            .Validate(
                options => options.BaseUrl is { IsAbsoluteUri: true } && options.BaseUrl.AbsolutePath.EndsWith('/'),
                "OpenDota:BaseUrl must be an absolute URL ending with '/'.")
            .Validate(options => options.Timeout > TimeSpan.Zero, "OpenDota:Timeout must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<OpenDotaClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<OpenDotaOptions>>().Value;
            client.BaseAddress = options.BaseUrl;
            client.Timeout = options.Timeout;
        });

        services.AddScoped<IMatchProvider, OpenDotaMatchProvider>();
        services.AddScoped<IReplayAnalyzer, OpenDotaReplayAnalyzer>();
        services.AddScoped<IHeroCatalog, OpenDotaHeroCatalog>();
        services.AddScoped<IItemCatalog, OpenDotaItemCatalog>();
        services.AddSingleton<CatalogLoadLocks>();
    }

    private static void AddDatabase(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        // Applies migrations (first attempt before the app starts, retries in the background); also logs a
        // warning when there is no database at all. Registered before AddDataProtection below on purpose:
        // hosted services start in order, so the keys table exists before Data Protection reads it.
        services.AddHostedService<DatabaseMigrator>();

        var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // The database is an extra layer: without it the app still works, on OpenDota alone.
            // Locally: dotnet user-secrets set "ConnectionStrings:Database" "..." --project DotaAnalyst.Server.
            services.AddSingleton<NoDatabase>();
            services.AddSingleton<IMatchRepository>(provider => provider.GetRequiredService<NoDatabase>());
            services.AddSingleton<IPlayerSearchRepository>(provider => provider.GetRequiredService<NoDatabase>());
            services.AddSingleton<ICatalogStore>(provider => provider.GetRequiredService<NoDatabase>());
            return;
        }

        // A factory instead of a single DbContext per request: repositories open one short-lived DbContext per
        // operation, so parallel lookups (match, heroes, items) never share one. It also registers AppDbContext
        // itself (Scoped), which Data Protection uses below.
        services.AddDbContextFactory<AppDbContext>(options => AppDbContext.Configure(options, connectionString));

        // Singleton: the "database is down, skip it for a while" state must be shared by every request.
        services.AddSingleton<StorageGuard>();
        services.AddScoped<IMatchRepository, MatchRepository>();
        services.AddScoped<IPlayerSearchRepository, PlayerSearchRepository>();
        services.AddScoped<ICatalogStore, CatalogStore>();

        // Keys that encrypt cookies (the future Steam login). Kept in the database, so a deploy or a restart of
        // the container doesn't throw them away and log everyone out. The fixed application name keeps the keys
        // valid if the app's folder or version changes.
        services.AddDataProtection()
            .SetApplicationName("DotaAnalyst")
            .PersistKeysToDbContext<AppDbContext>();
    }
}
