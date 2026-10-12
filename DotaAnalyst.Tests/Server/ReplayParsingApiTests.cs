using System.Net;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Tests.Domain;

namespace DotaAnalyst.Tests.Server;

// The whole path of a replay through the API: the analysis queues it, the worker parses it in the background
// (fake parser, no network) and a later visit shows the laning phase.
public class ReplayParsingApiTests
{
    private const long MyAccountId = 99517715;
    private const long MatchIdValue = 9034320024;
    private static readonly string AnalysisPath = $"/api/v1/players/{MyAccountId}/matches/{MatchIdValue}";

    [Fact]
    public async Task MatchAnalysis_UnparsedMatch_GoesFromProcessingToReady()
    {
        using var factory = new ApiFactory(replayParsing: true);
        factory.Analyzer.Matches[MatchIdValue] = CreateUnparsedMatch();
        factory.ReplayParser.Result = ReplayBuilder.Analysis(
            MatchIdValue,
            ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom, netWorth: 3000),
            ReplayBuilder.Hero(28, Team.Dire, Lane.Bottom, netWorth: 2000));
        var client = factory.CreateClient();

        // First visit: queued. Second visit while it is parsed: still processing, not queued twice.
        Assert.Equal("processing", await GetReplayStatusAsync(client));
        Assert.Equal("processing", await GetReplayStatusAsync(client));

        factory.ReplayParser.Release();
        var json = await WaitForReplayStatusAsync(client, "ready");

        var laning = json.GetProperty("laning");
        Assert.Equal("bottom", laning.GetProperty("lane").GetString());
        Assert.Equal("won", laning.GetProperty("outcome").GetString());
        Assert.Equal(SavedReplayState.Ready, factory.ReplayAnalyses.Results[MatchIdValue].State);
    }

    [Fact]
    public async Task MatchAnalysis_ParserDown_GoesFromProcessingToUnavailable_WithoutSavingAnything()
    {
        using var factory = new ApiFactory(replayParsing: true);
        factory.Analyzer.Matches[MatchIdValue] = CreateUnparsedMatch();
        // E.g. the machine running the parser is off and ngrok answered.
        factory.ReplayParser.Fail = new ReplayParserUnavailableException("The machine is off.");
        var client = factory.CreateClient();

        Assert.Equal("processing", await GetReplayStatusAsync(client));
        factory.ReplayParser.Release();

        // The circuit breaker is open now: nothing is queued again for a while.
        await WaitForReplayStatusAsync(client, "unavailable");
        Assert.Empty(factory.ReplayAnalyses.Results);
    }

    [Fact]
    public async Task MatchAnalysis_UnreadableReplay_IsFailed()
    {
        using var factory = new ApiFactory(replayParsing: true);
        factory.Analyzer.Matches[MatchIdValue] = CreateUnparsedMatch();
        factory.ReplayParser.Fail = new ReplayUnreadableException("Corrupt replay.");
        var client = factory.CreateClient();

        Assert.Equal("processing", await GetReplayStatusAsync(client));
        factory.ReplayParser.Release();

        await WaitForReplayStatusAsync(client, "failed");
        Assert.Equal(SavedReplayState.Failed, factory.ReplayAnalyses.Results[MatchIdValue].State);
    }

    private static async Task<string?> GetReplayStatusAsync(HttpClient client) =>
        (await GetAnalysisAsync(client)).GetProperty("replayStatus").GetString();

    private static async Task<JsonElement> GetAnalysisAsync(HttpClient client)
    {
        var response = await client.GetAsync(AnalysisPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Polls like the React page does, until the status arrives (or the test gives up).
    private static async Task<JsonElement> WaitForReplayStatusAsync(HttpClient client, string status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var json = await GetAnalysisAsync(client);
            if (json.GetProperty("replayStatus").GetString() == status)
            {
                return json;
            }

            // Not too often: the API's rate limit also applies to the test client.
            await Task.Delay(50, timeout.Token);
        }
    }

    private static MatchDetails CreateUnparsedMatch()
    {
        var me = MatchPlayerBuilder.Create(accountId: MatchPlayerBuilder.Account(MyAccountId), heroId: 31, team: Team.Radiant);
        return new MatchDetails(
            ReplayBuilder.MatchIdOf(MatchIdValue), TimeSpan.FromSeconds(1725), DateTimeOffset.UnixEpoch, Team.Radiant,
            60, 22, IsParsed: false, [me],
            ReplayUrl: new Uri("http://replay202.valve.net/570/9034320024_96247495.dem.bz2"));
    }
}
