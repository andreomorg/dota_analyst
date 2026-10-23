using System.Collections.Concurrent;
using System.Net;
using System.Text;
using AspNet.Security.OpenId.Steam;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Domain.Replays;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DotaAnalyst.Tests.Server;

// Runs the real API in memory, with OpenDota and the database replaced by fakes: no test touches the
// internet or a database here (the repositories have their own tests against PostgreSQL).
// replayParsing: turns on the real replay queue and worker, with ReplayParser below in place of the Go service
// (without it, replay parsing stays off, as in an app without ReplayParser:BaseUrl).
internal sealed class ApiFactory(IReadOnlyDictionary<string, string?>? settings = null, bool replayParsing = false)
    : WebApplicationFactory<Program>
{
    public FakeMatchProvider Matches { get; } = new();

    public FakeReplayAnalyzer Analyzer { get; } = new();

    public FakeMatchRepository SavedMatches { get; } = new();

    public FakePlayerSearchRepository PlayerSearches { get; } = new();

    // Saved replay results. Unless replayParsing is on, the parser and the queue stay the app's own (replay
    // parsing off), so a match without a saved result is "unavailable".
    public FakeReplayAnalysisRepository ReplayAnalyses { get; } = new();

    // Stands in for the Go service when replayParsing is on.
    public FakeReplayParser ReplayParser { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // No database: an empty connection string wins over the developer's user-secrets (which the
        // Development environment of the tests would otherwise load) and makes the app run without one.
        // Same for the Steam Web API key: profiles come from the fake below.
        builder.UseSetting("ConnectionStrings:Database", "");
        builder.UseSetting("Steam:ApiKey", "");
        // Tests run without permission to write to the Windows Event Log.
        builder.UseSetting("Logging:EventLog:LogLevel:Default", "None");
        // Same for the replay parser: never the developer's real one.
        builder.UseSetting("ReplayParser:BaseUrl", "");

        // UseSetting (not ConfigureAppConfiguration) so values read while Program builds the app also see them.
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            // Registered last, so they win over the OpenDota and database implementations.
            services.AddSingleton<IMatchProvider>(Matches);
            services.AddSingleton<IReplayAnalyzer>(Analyzer);
            services.AddSingleton<IHeroCatalog>(new FakeHeroCatalog());
            services.AddSingleton<IAbilityCatalog>(Abilities);
            services.AddSingleton<IItemCatalog>(new FakeItemCatalog());
            services.AddSingleton<IMatchRepository>(SavedMatches);
            services.AddSingleton<IPlayerSearchRepository>(PlayerSearches);
            services.AddSingleton<IReplayAnalysisRepository>(ReplayAnalyses);
            services.AddSingleton<ISteamProfileProvider>(SteamProfiles);
            if (replayParsing)
            {
                // The app's real queue, worker and circuit breaker, over the fake parser and repository.
                DotaAnalyst.Infrastructure.DependencyInjection.AddReplayParseQueue(services);
                services.AddSingleton<IReplayParser>(ReplayParser);
            }

            // Server-to-Steam calls of the login (OpenID discovery and check_authentication) go to a fake Steam.
            services.Configure<SteamAuthenticationOptions>(
                SteamAuthenticationDefaults.AuthenticationScheme,
                options => options.BackchannelHttpHandler = Steam);
        });
    }

    public KitApiCatalog Abilities { get; } = new();

    public FakeSteamOpenId Steam { get; } = new();

    public FakeSteamProfiles SteamProfiles { get; } = new();

    // Answers like steamcommunity.com/openid does: the discovery document, and the confirmation of a login.
    public sealed class FakeSteamOpenId : HttpMessageHandler
    {
        // What Steam says when asked "did you really sign this login?".
        public bool LoginIsValid { get; set; } = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = request.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"ns:http://specs.openid.net/auth/2.0\nis_valid:{(LoginIsValid ? "true" : "false")}\n"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        <?xml version="1.0" encoding="UTF-8"?>
                        <xrds:XRDS xmlns:xrds="xri://$xrds" xmlns="xri://$xrd*($v*2.0)">
                          <XRD>
                            <Service priority="0">
                              <Type>http://specs.openid.net/auth/2.0/server</Type>
                              <URI>https://steamcommunity.com/openid/login</URI>
                            </Service>
                          </XRD>
                        </xrds:XRDS>
                        """,
                        Encoding.UTF8,
                        "application/xrds+xml"),
                };
            return Task.FromResult(response);
        }
    }

    public sealed class FakeSteamProfiles : ISteamProfileProvider
    {
        public PlayerProfile? Profile { get; set; }

        public bool Fail { get; set; }

        public Task<PlayerProfile?> GetProfileAsync(AccountId accountId, CancellationToken cancellationToken) =>
            Fail ? throw new DataSourceUnavailableException("Steam is down.") : Task.FromResult(Profile);
    }

    public sealed class FakeMatchRepository : IMatchRepository
    {
        public ConcurrentDictionary<long, SavedMatch> Matches { get; } = new();

        public Task<SavedMatch?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult(Matches.GetValueOrDefault(matchId.Value));

        public Task SaveAsync(MatchDetails match, DateTimeOffset savedAt, CancellationToken cancellationToken)
        {
            Matches[match.MatchId.Value] = new SavedMatch(match, savedAt);
            return Task.CompletedTask;
        }
    }

    public sealed class FakeReplayAnalysisRepository : IReplayAnalysisRepository
    {
        public ConcurrentDictionary<long, SavedReplayAnalysis> Results { get; } = new();

        public Task<SavedReplayAnalysis?> FindAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult(Results.GetValueOrDefault(matchId.Value));

        public Task SaveAsync(SavedReplayAnalysis result, CancellationToken cancellationToken)
        {
            Results[result.MatchId.Value] = result;
            return Task.CompletedTask;
        }
    }

    // Answers (Result, or throws Fail) only once Release is called, so a test can see the replay "processing" first.
    public sealed class FakeReplayParser : IReplayParser
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ReplayAnalysis? Result { get; set; }

        public Exception? Fail { get; set; }

        public void Release() => release.TrySetResult();

        public async Task<ReplayAnalysis> ParseAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken)
        {
            await release.Task.WaitAsync(cancellationToken);
            if (Fail is not null)
            {
                throw Fail;
            }

            return Result ?? throw new InvalidOperationException("The test set no result for the fake parser.");
        }
    }

    public sealed class FakePlayerSearchRepository : IPlayerSearchRepository
    {
        public ConcurrentQueue<AccountId> Searches { get; } = new();

        public Task RecordSearchAsync(AccountId accountId, DateTimeOffset searchedAt, CancellationToken cancellationToken)
        {
            Searches.Enqueue(accountId);
            return Task.CompletedTask;
        }
    }

    public sealed class FakeMatchProvider : IMatchProvider
    {
        public IReadOnlyList<PlayerMatch> Result { get; set; } = [];

        public bool Fail { get; set; }

        public Task<IReadOnlyList<PlayerMatch>> GetRecentMatchesAsync(
            AccountId accountId, CancellationToken cancellationToken) =>
            Fail ? throw new DataSourceUnavailableException("OpenDota is down.") : Task.FromResult(Result);
    }

    public sealed class FakeReplayAnalyzer : IReplayAnalyzer
    {
        public Dictionary<long, MatchDetails> Matches { get; } = [];

        public bool Fail { get; set; }

        public Task<MatchDetails?> GetMatchDetailsAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Fail
                ? throw new DataSourceUnavailableException("OpenDota is down.")
                : Task.FromResult(Matches.GetValueOrDefault(matchId.Value));
    }

    private sealed class FakeHeroCatalog : IHeroCatalog
    {
        public Task<IReadOnlyDictionary<int, Hero>> GetHeroesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, Hero>>(new Dictionary<int, Hero> { [31] = new(31, "Lich") });
    }

    private sealed class FakeItemCatalog : IItemCatalog
    {
        public Task<IReadOnlyDictionary<int, Item>> GetItemsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, Item>>(new Dictionary<int, Item>
            {
                [1] = new(1, "Blink Dagger")
                {
                    Details = new ItemDetails("blink", 2250, [], false)
                    {
                        Behavior = ["Point Target"],
                        Cooldown = 15,
                        Abilities = [new(ItemAbilityKind.Active, "Blink", "Teleport to a target point up to 1200 units away.")],
                    },
                },
            });
    }
}
