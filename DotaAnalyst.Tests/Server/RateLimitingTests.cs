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
    public async Task SteamLoginCallback_IsRateLimited()
    {
        // The callback isn't a controller (the authentication middleware answers it) but makes the server call Steam.
        using var factory = CreateFactory(permitLimit: 2);
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await client.GetAsync("/api/auth/steam/callback");
        await client.GetAsync("/api/auth/steam/callback");
        var rejected = await client.GetAsync("/api/auth/steam/callback");

        PlayersApiTests.AssertProblem(rejected, HttpStatusCode.TooManyRequests);
        // Its own counter: logging in doesn't use up the user's API requests.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MatchesUrl)).StatusCode);
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

    [Fact]
    public async Task WithCustomClientIpHeader_UsesItAndIgnoresXForwardedFor()
    {
        // Render setup: the client IP comes from CF-Connecting-IP; X-Forwarded-For can be faked by clients.
        using var factory = CreateFactory(permitLimit: 1, forwardedHeaders: true, clientIpHeader: "CF-Connecting-IP");
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await SendFromAsync(client, "203.0.113.1", "CF-Connecting-IP", fakeForwardedFor: "198.51.100.1")).StatusCode);
        // Same real client with a different fake X-Forwarded-For: still limited.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendFromAsync(client, "203.0.113.1", "CF-Connecting-IP", fakeForwardedFor: "198.51.100.2")).StatusCode);
        // Another real client: its own limit.
        Assert.Equal(HttpStatusCode.OK, (await SendFromAsync(client, "203.0.113.2", "CF-Connecting-IP")).StatusCode);
    }

    private static ApiFactory CreateFactory(int permitLimit, bool forwardedHeaders = false, string? clientIpHeader = null) =>
        new(new Dictionary<string, string?>
        {
            ["RateLimiting:PermitLimit"] = permitLimit.ToString(),
            ["RateLimiting:Window"] = "00:10:00",
            ["ForwardedHeaders:Enabled"] = forwardedHeaders.ToString(),
            ["ForwardedHeaders:ForwardedForHeaderName"] = clientIpHeader ?? "X-Forwarded-For",
        });

    private static Task<HttpResponseMessage> SendFromAsync(
        HttpClient client, string clientIp, string header = "X-Forwarded-For", string? fakeForwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, MatchesUrl);
        request.Headers.Add(header, clientIp);
        if (fakeForwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", fakeForwardedFor);
        }

        return client.SendAsync(request);
    }
}
