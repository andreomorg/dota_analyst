using System.Net;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Tests.Domain;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DotaAnalyst.Tests.Server;

public class PlayersApiTests
{
    private const long MyAccountId = 99517715;
    private const long MatchIdValue = 9034320024;

    [Fact]
    public async Task RecentMatches_ReturnsMatches()
    {
        using var factory = new ApiFactory();
        factory.Matches.Result =
        [
            new PlayerMatch(MatchIdValue, 31, Team.Radiant, Team.Radiant, new Kda(2, 4, 19), TimeSpan.FromSeconds(1725), DateTimeOffset.UnixEpoch),
        ];

        var response = await factory.CreateClient().GetAsync($"/api/v1/players/{MyAccountId}/matches");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = Assert.Single((await ReadJsonAsync(response)).EnumerateArray());
        Assert.Equal("Lich", match.GetProperty("heroName").GetString());
        Assert.True(match.GetProperty("won").GetBoolean());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("99999999999999999999")]
    public async Task RecentMatches_RejectsInvalidAccountId(string id)
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync($"/api/v1/players/{id}/matches");

        AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RecentMatches_Returns502WhenOpenDotaFails()
    {
        using var factory = new ApiFactory();
        factory.Matches.Fail = true;

        var response = await factory.CreateClient().GetAsync($"/api/v1/players/{MyAccountId}/matches");

        AssertProblem(response, HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task MatchAnalysis_ReturnsAnalysisWithEnumsAsCamelCaseStrings()
    {
        using var factory = new ApiFactory();
        factory.Analyzer.Matches[MatchIdValue] = CreateMatch();

        var response = await factory.CreateClient().GetAsync($"/api/v1/players/{MyAccountId}/matches/{MatchIdValue}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.True(json.GetProperty("radiantWon").GetBoolean());
        var me = Assert.Single(json.GetProperty("radiant").EnumerateArray());
        Assert.True(me.GetProperty("isCurrentPlayer").GetBoolean());
        Assert.Equal("Blink Dagger", me.GetProperty("items")[0].GetString());

        var performance = json.GetProperty("performance");
        Assert.Equal("deniesPerMinute", performance.GetProperty("strengths")[0].GetString());
        Assert.Equal("strong", performance.GetProperty("metrics")[0].GetProperty("rating").GetString());

        // Unparsed, but replay parsing is off.
        Assert.Equal("unavailable", json.GetProperty("replayStatus").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("laning").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("draft").ValueKind);
    }

    [Fact]
    public async Task MatchAnalysis_DraftHasAdditiveCamelCaseProfileAndSummary()
    {
        using var factory = new ApiFactory();
        factory.Analyzer.Matches[MatchIdValue] = CreateMatch();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IHeroCatalog>(new DraftHeroCatalog())));

        var response = await app.CreateClient().GetAsync($"/api/v1/players/{MyAccountId}/matches/{MatchIdValue}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = (await ReadJsonAsync(response)).GetProperty("draft");
        Assert.Equal(["teams", "enemies"], draft.EnumerateObject().Select(property => property.Name));
        Assert.Equal(0, draft.GetProperty("enemies").GetArrayLength());
        var team = draft.GetProperty("teams")[0];
        Assert.True(team.GetProperty("isPlayersTeam").GetBoolean());
        Assert.Equal("radiant", team.GetProperty("team").GetString());
        var hero = team.GetProperty("heroes")[0];
        Assert.Equal(31, hero.GetProperty("heroId").GetInt32());
        Assert.Equal("intelligence", hero.GetProperty("attribute").GetString());
        Assert.Equal("ranged", hero.GetProperty("attackType").GetString());
        Assert.Equal("support", hero.GetProperty("roles")[0].GetString());
        var summary = team.GetProperty("summary");
        var support = summary.GetProperty("roles").EnumerateArray().Single(row => row.GetProperty("role").GetString() == "support");
        Assert.Equal(1, support.GetProperty("count").GetInt32());
        Assert.Equal("melee", summary.GetProperty("attackTypes")[0].GetProperty("attackType").GetString());
        Assert.Equal("strength", summary.GetProperty("attributes")[0].GetProperty("attribute").GetString());
    }

    private sealed class DraftHeroCatalog : IHeroCatalog
    {
        public Task<IReadOnlyDictionary<int, Hero>> GetHeroesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, Hero>>(new Dictionary<int, Hero>
            {
                [31] = new(31, "Lich")
                {
                    Details = new HeroDetails(HeroAttribute.Intelligence,
                    HeroAttackType.Ranged, new HashSet<HeroRole> { HeroRole.Support })
                },
            });
    }

    [Fact]
    public async Task MatchAnalysis_ReadyReplay_ReturnsTheLaningPhase()
    {
        using var factory = new ApiFactory();
        factory.Analyzer.Matches[MatchIdValue] = CreateMatch();
        var replay = ReplayBuilder.Analysis(
            MatchIdValue,
            new LaningStats(31, Team.Radiant, Lane.Bottom, lastHits: 12, denies: 9, netWorth: 2047, experience: 2609, deaths: 2),
            ReplayBuilder.Hero(17, Team.Radiant, Lane.Bottom, netWorth: 3000),
            ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom, netWorth: 3797));
        factory.ReplayAnalyses.Results[MatchIdValue] = SavedReplayAnalysis.Ready(replay, DateTimeOffset.UnixEpoch);

        var response = await factory.CreateClient().GetAsync($"/api/v1/players/{MyAccountId}/matches/{MatchIdValue}");

        // The contract the React page reads.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("ready", json.GetProperty("replayStatus").GetString());
        var laning = json.GetProperty("laning");
        Assert.Equal(
            ["endsAtSeconds", "lane", "outcome", "netWorthDifference", "player", "allies", "opponents"],
            laning.EnumerateObject().Select(property => property.Name));
        Assert.Equal(600, laning.GetProperty("endsAtSeconds").GetInt32());
        Assert.Equal("bottom", laning.GetProperty("lane").GetString());
        Assert.Equal("won", laning.GetProperty("outcome").GetString());
        Assert.Equal(1250, laning.GetProperty("netWorthDifference").GetInt32());
        var player = laning.GetProperty("player");
        Assert.Equal(
            ["heroId", "heroName", "lastHits", "denies", "netWorth", "experience", "deaths"],
            player.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Lich", player.GetProperty("heroName").GetString());
        Assert.Equal(2609, player.GetProperty("experience").GetInt32());
        Assert.Equal(17, Assert.Single(laning.GetProperty("allies").EnumerateArray()).GetProperty("heroId").GetInt32());
        Assert.Equal(28, Assert.Single(laning.GetProperty("opponents").EnumerateArray()).GetProperty("heroId").GetInt32());
    }

    [Fact]
    public async Task MatchAnalysis_ReadyReplay_ReturnsEveryStatisticPerMinute()
    {
        using var factory = new ApiFactory();
        factory.Analyzer.Matches[MatchIdValue] = CreateMatch();
        var replay = ReplayBuilder.Analysis(
            MatchIdValue,
            new LaningStats(31, Team.Radiant, Lane.Bottom, lastHits: 12, denies: 9, netWorth: 2047, experience: 2609, deaths: 2),
            ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom, netWorth: 3797));
        factory.ReplayAnalyses.Results[MatchIdValue] = SavedReplayAnalysis.Ready(replay, DateTimeOffset.UnixEpoch);

        var response = await factory.CreateClient().GetAsync($"/api/v1/players/{MyAccountId}/matches/{MatchIdValue}");

        // The contract the React page reads: the catalog, the points and each hero's series.
        var stats = (await ReadJsonAsync(response)).GetProperty("stats");
        Assert.Equal(["definitions", "seconds", "heroes"], stats.EnumerateObject().Select(property => property.Name));
        var deaths = stats.GetProperty("definitions").EnumerateArray().Single(stat => stat.GetProperty("key").GetString() == "deaths");
        Assert.Equal(
            ("replayMinutes", "total", "count", "lower"),
            (deaths.GetProperty("source").GetString(), deaths.GetProperty("kind").GetString(),
                deaths.GetProperty("unit").GetString(), deaths.GetProperty("better").GetString()));
        Assert.Equal([0, 600, 1200], stats.GetProperty("seconds").EnumerateArray().Select(value => value.GetInt32()));
        var me = stats.GetProperty("heroes").EnumerateArray().Single(hero => hero.GetProperty("isCurrentPlayer").GetBoolean());
        Assert.Equal(("Lich", "radiant"), (me.GetProperty("heroName").GetString(), me.GetProperty("team").GetString()));
        var lastHits = me.GetProperty("series").EnumerateArray().Single(series => series.GetProperty("stat").GetString() == "lastHits");
        Assert.Equal([0, 12, 24], lastHits.GetProperty("values").EnumerateArray().Select(value => value.GetInt32()));
    }

    [Theory]
    [InlineData(MyAccountId, 0L, HttpStatusCode.BadRequest)]
    [InlineData(0L, MatchIdValue, HttpStatusCode.BadRequest)]
    [InlineData(MyAccountId, 1L, HttpStatusCode.NotFound)]
    public async Task MatchAnalysis_ReturnsProblemForInvalidOrMissingMatch(long accountId, long matchId, HttpStatusCode expected)
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync($"/api/v1/players/{accountId}/matches/{matchId}");

        AssertProblem(response, expected);
    }

    [Fact]
    public async Task MatchAnalysis_Returns502WhenOpenDotaFails()
    {
        using var factory = new ApiFactory();
        factory.Analyzer.Fail = true;

        var response = await factory.CreateClient().GetAsync($"/api/v1/players/{MyAccountId}/matches/{MatchIdValue}");

        AssertProblem(response, HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task MatchAnalysis_SavedMatchIsServedEvenWhenOpenDotaFails()
    {
        using var factory = new ApiFactory();
        // Saved long ago and unparsed: OpenDota is asked again, fails, and the saved copy is used.
        factory.SavedMatches.Matches[MatchIdValue] = new SavedMatch(CreateMatch(), DateTimeOffset.UnixEpoch);
        factory.Analyzer.Fail = true;

        var response = await factory.CreateClient().GetAsync($"/api/v1/players/{MyAccountId}/matches/{MatchIdValue}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RecentMatches_RecordsTheSearchedAccount()
    {
        using var factory = new ApiFactory();
        const long steamId64 = MyAccountId + 76561197960265728;

        await factory.CreateClient().GetAsync($"/api/v1/players/{steamId64}/matches");

        // Recorded as the account ID, even when searched by SteamID64.
        Assert.Equal(MyAccountId, Assert.Single(factory.PlayerSearches.Searches).Value);
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404Problem()
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/does-not-exist");

        AssertProblem(response, HttpStatusCode.NotFound);
    }

    internal static void AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static MatchDetails CreateMatch()
    {
        MatchId.TryCreate(MatchIdValue, out var matchId);
        var me = MatchPlayerBuilder.Create(
            accountId: MatchPlayerBuilder.Account(MyAccountId),
            heroId: 31,
            team: Team.Radiant,
            itemIds: [1],
            benchmarks: [new Benchmark(BenchmarkMetric.DeniesPerMinute, 0.63, 0.97)]);
        return new MatchDetails(matchId, TimeSpan.FromSeconds(1725), DateTimeOffset.UnixEpoch, Team.Radiant, 60, 22, false, [me]);
    }
}
