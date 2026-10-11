using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DotaAnalyst.Tests.Server;

// Runs the real API in memory, with OpenDota replaced by fakes: no test touches the internet.
internal sealed class ApiFactory(IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public FakeMatchProvider Matches { get; } = new();

    public FakeReplayAnalyzer Analyzer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting (not ConfigureAppConfiguration) so values read while Program builds the app also see them.
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            // Registered last, so they win over the OpenDota implementations.
            services.AddSingleton<IMatchProvider>(Matches);
            services.AddSingleton<IReplayAnalyzer>(Analyzer);
            services.AddSingleton<IHeroCatalog>(new FakeHeroCatalog());
            services.AddSingleton<IItemCatalog>(new FakeItemCatalog());
        });
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
            Task.FromResult<IReadOnlyDictionary<int, Item>>(new Dictionary<int, Item> { [1] = new(1, "Blink Dagger") });
    }
}
