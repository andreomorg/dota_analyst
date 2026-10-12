using System.Net;
using System.Text;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Infrastructure.ReplayParser;
using DotaAnalyst.Tests.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Tests.Infrastructure;

// The client against a fake parser (no network): the contract of replay-parser/CLAUDE.md, and above all that
// only the parser's own problem+json errors say something about the replay. ngrok answering for a machine that
// is off (even with a 404) must never become "replay expired".
public class ReplayParserClientTests
{
    private const string ApiKey = "secret-key";
    private const long MatchIdValue = 9034320024;
    private static readonly MatchId Match = ReplayBuilder.MatchIdOf(MatchIdValue);
    private static readonly Uri ReplayUrl = new("http://replay202.valve.net/570/9034320024_96247495.dem.bz2");

    private const string LaningJson = """
        {
          "laningEndsAtSeconds": 600,
          "players": [
            { "heroId": 31, "steamId": "76561198059783443", "isRadiant": true, "lane": "bottom",
              "lastHits": 12, "denies": 3, "netWorth": 2900, "xp": 3100, "deaths": 1 },
            { "heroId": 28, "steamId": "76561198000000000", "isRadiant": false, "lane": "jungle",
              "lastHits": 40, "denies": 0, "netWorth": 3500, "xp": 2800, "deaths": 0 }
          ]
        }
        """;

    [Fact]
    public async Task Parse_SendsTheContractRequest_AndMapsTheAnswer()
    {
        var parser = new FakeParser(Json(HttpStatusCode.OK, LaningJson, "application/json"));

        var analysis = await CreateClient(parser).ParseAsync(Match, ReplayUrl, CancellationToken.None);

        // Request: POST /v1/laning with the token, ngrok's header and the replay address.
        var request = Assert.Single(parser.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://parser.example/v1/laning", request.Uri);
        Assert.Equal($"Bearer {ApiKey}", request.Authorization);
        Assert.Equal("1", request.NgrokSkipBrowserWarning);
        Assert.Equal(ReplayUrl.AbsoluteUri, JsonDocument.Parse(request.Body).RootElement.GetProperty("replayUrl").GetString());

        // Answer -> domain.
        Assert.Equal(Match, analysis.MatchId);
        Assert.Equal(600, analysis.EndsAtSeconds);
        Assert.Equal(new LaningStats(31, Team.Radiant, Lane.Bottom, 12, 3, 2900, 3100, 1), analysis.FindHero(31));
        Assert.Equal(new LaningStats(28, Team.Dire, Lane.Jungle, 40, 0, 3500, 2800, 0), analysis.FindHero(28));
    }

    [Fact]
    public async Task Parse_WithoutApiKey_SendsNoAuthorization()
    {
        // The parser accepts that only when run locally without a key (go run).
        var parser = new FakeParser(Json(HttpStatusCode.OK, LaningJson, "application/json"));

        await CreateClient(parser, apiKey: "").ParseAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Null(Assert.Single(parser.Requests).Authorization);
    }

    [Fact]
    public async Task Parse_Problem404_IsAnExpiredReplay()
    {
        var client = CreateClient(new FakeParser(Problem(HttpStatusCode.NotFound)));

        await Assert.ThrowsAsync<ReplayExpiredException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)] // the replay can't be read
    [InlineData(HttpStatusCode.BadRequest)] // the parser refused our request (our bug)
    public async Task Parse_Problem422Or400_IsAnUnreadableReplay(HttpStatusCode status)
    {
        var client = CreateClient(new FakeParser(Problem(status)));

        await Assert.ThrowsAsync<ReplayUnreadableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)] // wrong key: configuration, not the replay
    [InlineData(HttpStatusCode.BadGateway)] // could not download from Valve
    [InlineData(HttpStatusCode.ServiceUnavailable)] // busy
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Parse_OtherProblems_MeanTheParserIsUnavailable(HttpStatusCode status)
    {
        var client = CreateClient(new FakeParser(Problem(status)));

        var exception = await Assert.ThrowsAsync<ReplayParserUnavailableException>(
            () => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
        Assert.DoesNotContain(ApiKey, exception.ToString());
    }

    [Fact]
    public async Task Parse_NgrokPageWith404_IsUnavailable_NotExpired()
    {
        // What ngrok answers by itself when the tunnel is offline (the machine running the parser is off).
        var response = Json(HttpStatusCode.NotFound, "<!DOCTYPE html><html><body>ERR_NGROK_3200</body></html>", "text/html");
        response.Headers.Add("ngrok-error-code", "ERR_NGROK_3200");
        var client = CreateClient(new FakeParser(response));

        await Assert.ThrowsAsync<ReplayParserUnavailableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "text/plain", "Tunnel not found")]
    [InlineData(HttpStatusCode.NotFound, "application/json", """{"error": "not found"}""")] // JSON, but not ProblemDetails
    [InlineData(HttpStatusCode.BadGateway, "text/html", "<html>Bad gateway</html>")]
    [InlineData(HttpStatusCode.OK, "text/html", "<html>You are about to visit...</html>")] // ngrok's warning page
    [InlineData(HttpStatusCode.Found, "text/html", "")] // a redirect
    public async Task Parse_AnswersNotFromTheParser_AreUnavailable(HttpStatusCode status, string contentType, string body)
    {
        var client = CreateClient(new FakeParser(Json(status, body, contentType)));

        await Assert.ThrowsAsync<ReplayParserUnavailableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Parse_ProblemWithNgrokErrorHeader_IsUnavailable()
    {
        // Anything carrying ngrok's error header was answered by ngrok, whatever it looks like.
        var response = Problem(HttpStatusCode.NotFound);
        response.Headers.Add("ngrok-error-code", "ERR_NGROK_8012");
        var client = CreateClient(new FakeParser(response));

        await Assert.ThrowsAsync<ReplayParserUnavailableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Parse_ConnectionRefused_IsUnavailable()
    {
        var client = CreateClient(new FakeParser(_ => Task.FromException<HttpResponseMessage>(
            new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused"))));

        await Assert.ThrowsAsync<ReplayParserUnavailableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Parse_Timeout_IsUnavailable()
    {
        var parser = new FakeParser(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Json(HttpStatusCode.OK, LaningJson, "application/json");
        });
        var client = CreateClient(parser, timeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<ReplayParserUnavailableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Parse_CancelledByTheCaller_IsNotTranslated()
    {
        // The app stopping is not "the parser is unavailable".
        var parser = new FakeParser(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Json(HttpStatusCode.OK, LaningJson, "application/json");
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(parser).ParseAsync(Match, ReplayUrl, cancellation.Token));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"players": []}""")] // no laningEndsAtSeconds
    [InlineData("""{"laningEndsAtSeconds": 600}""")] // no players
    [InlineData("""{"laningEndsAtSeconds": 700, "players": []}""")] // the laning phase ends at 10:00
    [InlineData("""{"laningEndsAtSeconds": 600, "players": [null]}""")]
    [InlineData("""{"laningEndsAtSeconds": 600, "players": [{"heroId": 31, "isRadiant": true, "lane": "bottom", "lastHits": -1, "denies": 0, "netWorth": 0, "xp": 0, "deaths": 0}]}""")]
    [InlineData("""{"laningEndsAtSeconds": 600, "players": [{"heroId": 31, "isRadiant": true, "lane": "sideways", "lastHits": 1, "denies": 0, "netWorth": 0, "xp": 0, "deaths": 0}]}""")]
    [InlineData("""{"laningEndsAtSeconds": 600, "players": [{"heroId": 31, "lane": "bottom", "lastHits": 1, "denies": 0, "netWorth": 0, "xp": 0, "deaths": 0}]}""")] // no isRadiant
    [InlineData("""{"laningEndsAtSeconds": 600, "players": [{"heroId": 31, "isRadiant": true, "lane": "bottom", "lastHits": 1, "denies": 0, "netWorth": 0, "deaths": 0}]}""")] // no xp
    [InlineData("""{"laningEndsAtSeconds": 600, "players": [{"heroId": 31, "isRadiant": true, "lane": "top", "lastHits": 1, "denies": 0, "netWorth": 0, "xp": 0, "deaths": 0}, {"heroId": 31, "isRadiant": false, "lane": "top", "lastHits": 1, "denies": 0, "netWorth": 0, "xp": 0, "deaths": 0}]}""")] // same hero twice
    public async Task Parse_MalformedOrInvalidAnswer_IsAnUnreadableReplay(string body)
    {
        var client = CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json")));

        await Assert.ThrowsAsync<ReplayUnreadableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    private static ReplayParserClient CreateClient(FakeParser parser, string apiKey = ApiKey, TimeSpan? timeout = null) =>
        new(
            new HttpClient(parser) { BaseAddress = new Uri("https://parser.example/"), Timeout = timeout ?? TimeSpan.FromSeconds(10) },
            Options.Create(new ReplayParserOptions { BaseUrl = new Uri("https://parser.example/"), ApiKey = apiKey }),
            NullLogger<ReplayParserClient>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string contentType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    private static HttpResponseMessage Problem(HttpStatusCode status) =>
        Json(status, $$"""{"title": "error", "status": {{(int)status}}, "detail": "details"}""", "application/problem+json");

    internal sealed record SentRequest(HttpMethod Method, string Uri, string? Authorization, string? NgrokSkipBrowserWarning, string Body);

    // Answers every request with the given function, and remembers what was sent.
    private sealed class FakeParser(Func<CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public FakeParser(HttpResponseMessage response) : this(_ => Task.FromResult(response))
        {
        }

        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("ngrok-skip-browser-warning", out var values) ? values.Single() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await answer(cancellationToken);
        }
    }
}
