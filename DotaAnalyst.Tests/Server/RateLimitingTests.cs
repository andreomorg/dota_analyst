using System.Net;

namespace DotaAnalyst.Tests.Server;

public class RateLimitingTests
{
    private const string MatchesUrl = "/api/v1/players/99517715/matches";

    [Fact]
    public async Task RequestsAboveTheLimit_Get429ProblemWithRetryAfter()
    {
        using var factory = CreateFactory(permitLimit: 2);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MatchesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MatchesUrl)).StatusCode);
        var rejected = await client.GetAsync(MatchesUrl);

        PlayersApiTests.AssertProblem(rejected, HttpStatusCode.TooManyRequests);
        Assert.NotNull(rejected.Headers.RetryAfter);
    }

    [Fact]
    public async Task HealthCheck_IsNotRateLimited()
    {
        using var factory = CreateFactory(permitLimit: 1);
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        }
    }

    [Fact]
    public async Task WithForwardedHeaders_EachClientIpHasItsOwnLimit()
    {
        using var factory = CreateFactory(permitLimit: 1, forwardedHeaders: true);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await SendFromAsync(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendFromAsync(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendFromAsync(client, "203.0.113.2")).StatusCode);
    }

    [Fact]
    public async Task WithoutForwardedHeaders_XForwardedForIsIgnored()
    {
        using var factory = CreateFactory(permitLimit: 1, forwardedHeaders: false);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await SendFromAsync(client, "203.0.113.1")).StatusCode);
        // A faked header must not give a fresh limit when there is no trusted proxy.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendFromAsync(client, "203.0.113.2")).StatusCode);
    }

    private static ApiFactory CreateFactory(int permitLimit, bool forwardedHeaders = false) =>
        new(new Dictionary<string, string?>
        {
            ["RateLimiting:PermitLimit"] = permitLimit.ToString(),
            ["RateLimiting:Window"] = "00:10:00",
            ["ForwardedHeaders:Enabled"] = forwardedHeaders.ToString(),
        });

    private static Task<HttpResponseMessage> SendFromAsync(HttpClient client, string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, MatchesUrl);
        request.Headers.Add("X-Forwarded-For", clientIp);
        return client.SendAsync(request);
    }
}
