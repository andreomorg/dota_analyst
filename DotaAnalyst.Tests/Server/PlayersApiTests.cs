using System.Net;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Tests.Domain;

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
