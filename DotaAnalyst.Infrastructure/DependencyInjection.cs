using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Infrastructure.Baseline;
using DotaAnalyst.Infrastructure.Caching;
using DotaAnalyst.Infrastructure.Catalogs;
using DotaAnalyst.Infrastructure.OpenDota;
using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Baseline;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;
using DotaAnalyst.Infrastructure.Persistence.Matches;
using DotaAnalyst.Infrastructure.Persistence.Players;
using DotaAnalyst.Infrastructure.Persistence.Replays;
using DotaAnalyst.Infrastructure.ReplayParser;
using DotaAnalyst.Infrastructure.Steam;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
        var hasDatabase = AddDatabase(services, configuration);
        var parsesReplays = AddReplayParsing(services, configuration, hasDatabase);
        AddBaselineCollector(services, configuration, hasDatabase && parsesReplays);

        return services;
    }

    // Our own reading of replays (the Go service, replay-parser/), for matches OpenDota didn't parse. Only on
    // when both its address and the database exist: without the database a result could never be kept, so the
    // same replay would be parsed on every visit. Off, analyses say replayStatus "unavailable". An invalid address
    // also turns it off (with a warning) instead of stopping the app: the rest of the site doesn't need the parser.
    // Returns whether replay parsing is on.
    private static bool AddReplayParsing(IServiceCollection services, IConfiguration configuration, bool hasDatabase)
    {
        var section = configuration.GetSection(ReplayParserOptions.SectionName);
        var baseUrl = section[nameof(ReplayParserOptions.BaseUrl)];
        var offReason =
            string.IsNullOrWhiteSpace(baseUrl) ? "ReplayParser:BaseUrl is not set"
            : !IsValidParserAddress(baseUrl) ? "ReplayParser:BaseUrl must be an absolute http(s) URL ending with '/'"
            : !hasDatabase ? "there is no database (ConnectionStrings:Database) to keep the results"
            : null;
        if (offReason is not null)
        {
            services.AddSingleton<IReplayParser, NoReplayParser>();
            services.AddSingleton<IReplayParseQueue, DisabledReplayParseQueue>();
            services.AddHostedService(provider => new ReplayParsingStartupWarning(
                $"Replay parsing is off: {offReason}", provider.GetRequiredService<ILogger<ReplayParsingStartupWarning>>()));
            return false;
        }

        // Allowed (a parser run locally without REPLAY_PARSER_API_KEY accepts it), but in production it is a mistake:
        // the parser would answer 401 and parsing would keep pausing.
        if (string.IsNullOrWhiteSpace(section[nameof(ReplayParserOptions.ApiKey)]))
        {
            services.AddHostedService(provider => new ReplayParsingStartupWarning(
                "ReplayParser:ApiKey is not set: requests to the replay parser carry no API key, which only a parser without REPLAY_PARSER_API_KEY accepts",
                provider.GetRequiredService<ILogger<ReplayParsingStartupWarning>>()));
        }

        services.AddOptions<ReplayParserOptions>()
            .Bind(section)
            .Validate(options => options.Timeout > TimeSpan.Zero, "ReplayParser:Timeout must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<IReplayParser, ReplayParserClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<ReplayParserOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.Timeout = options.Timeout;
                // The whole match is ~330 KB for 30 min; the parser itself refuses answers over 4 MB.
                client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            })
            // A redirect is not something our parser does: it is answered as "unavailable" instead of followed.
            // gzip: the parser compresses when asked (~330 KB → ~80 KB), which matters through the ngrok tunnel.
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip,
            })
            // The default HttpClient logs could write the request headers, and the API key travels in one.
            .RemoveAllLoggers();

        AddReplayParseQueue(services);
        return true;
    }

    // The collector of the comparison base (pro and high-rank matches). Off unless Collector:Enabled — it runs on the
    // project owner's machine, next to the replay parser, writing to the production database; never on Render. It
    // also needs a database and replay parsing.
    private static void AddBaselineCollector(IServiceCollection services, IConfiguration configuration, bool canCollect)
    {
        var section = configuration.GetSection(BaselineCollectorOptions.SectionName);
        var options = section.Get<BaselineCollectorOptions>() ?? new BaselineCollectorOptions();
        // Where the finished base is mirrored for production (Neon); empty = only the local site gets it.
        services.AddSingleton<IBaselineMirror>(provider => new BaselineMirror(
            configuration.GetConnectionString(BaselineMirror.ConnectionStringName), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(new BaselineCollectorSettings(
            options.DailyLimit,
            options.FocusAccountIds
                .Select(id => AccountId.TryCreate(id, out var account) ? account : (AccountId?)null)
                .OfType<AccountId>()
                .ToList(),
            Math.Max(0, options.TargetMatchesPerHero)));

        if (!options.Enabled)
        {
            return;
        }

        if (!canCollect)
        {
            services.AddHostedService(provider => new ReplayParsingStartupWarning(
                "Collector:Enabled is set, but the collector needs a database and replay parsing: it is off",
                provider.GetRequiredService<ILogger<ReplayParsingStartupWarning>>()));
            return;
        }

        services.AddHostedService<BaselineCollectorService>();
        services.AddHostedService<BaselinePublisherService>();
    }

    // Absolute http(s), ending with '/': without it "v1/match" would replace the last segment of the path.
    private static bool IsValidParserAddress(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var url)
        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
        && url.AbsolutePath.EndsWith('/');

    // The queue, its worker and the parser's circuit breaker. Separate so the API tests can turn them on with a
    // fake parser (they have no database nor a real parser).
    internal static void AddReplayParseQueue(IServiceCollection services)
    {
        // Singletons: one queue and one "parser is down" state for the whole app, shared by requests and the worker.
        services.AddSingleton<ReplayParserCircuitBreaker>();
        services.AddSingleton<ReplayParseQueue>();
        services.AddSingleton<IReplayParseQueue>(provider => provider.GetRequiredService<ReplayParseQueue>());
        services.AddHostedService<ReplayParseWorker>();
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
            .Validate(options => options.PermitsPerMinute > 0, "OpenDota:PermitsPerMinute must be positive.")
            .Validate(
                options => options.MaxQueueWait >= TimeSpan.Zero && options.MaxQueueWait < options.Timeout,
                "OpenDota:MaxQueueWait must be zero or more and less than OpenDota:Timeout.")
            .ValidateOnStart();

        // Singleton: one count of calls for the whole server, since OpenDota's quota is per IP (ours).
        services.AddSingleton<OpenDotaRateLimiter>();
        services.AddTransient<OpenDotaRateLimitHandler>();

        services.AddHttpClient<OpenDotaClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<OpenDotaOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.Timeout = options.Timeout;
            })
            .AddHttpMessageHandler<OpenDotaRateLimitHandler>();

        services.AddScoped<IMatchProvider, OpenDotaMatchProvider>();
        services.AddScoped<IReplayAnalyzer, OpenDotaReplayAnalyzer>();
        services.AddScoped<IHeroCatalog, OpenDotaHeroCatalog>();
        services.AddScoped<IItemCatalog, OpenDotaItemCatalog>();
        services.AddScoped<IBaselineMatchSource, OpenDotaBaselineMatchSource>();
        services.AddSingleton<CatalogLoadLocks>();
    }

    // Returns whether there is a database.
    private static bool AddDatabase(IServiceCollection services, IConfiguration configuration)
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
            services.AddSingleton<IReplayAnalysisRepository, NoReplayAnalyses>();
            services.AddSingleton<IBaselineRepository, NoBaseline>();
            services.AddSingleton<IBaselineDistributionStore, NoBaselineDistributions>();
            return false;
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
        // Also when replay parsing is off: results saved while it was on are still shown.
        services.AddScoped<IReplayAnalysisRepository, ReplayAnalysisRepository>();
        // The raw documents only where heavy data lives (ReplayParser:KeepDocuments, on in the local compose): production
        // keeps refined data only. Replay parsing only runs with a database (see AddReplayParsing).
        if (configuration.GetSection(ReplayParserOptions.SectionName).GetValue<bool>(nameof(ReplayParserOptions.KeepDocuments)))
        {
            services.AddScoped<IReplayDocumentArchive, ReplayDocumentArchive>();
        }
        else
        {
            services.AddSingleton<IReplayDocumentArchive, NoReplayDocumentArchive>();
        }
        // The comparison base: raw (written by the collector, on its machine only) and finished (read by every analysis;
        // in production a mirror published from the collector's machine).
        services.AddScoped<IBaselineRepository, BaselineRepository>();
        services.AddScoped<IBaselineDistributionStore, BaselineDistributionStore>();

        // Keys that encrypt cookies (the future Steam login). Kept in the database, so a deploy or a restart of
        // the container doesn't throw them away and log everyone out. The fixed application name keeps the keys
        // valid if the app's folder or version changes.
        services.AddDataProtection()
            .SetApplicationName("DotaAnalyst")
            .PersistKeysToDbContext<AppDbContext>();
        return true;
    }
}
