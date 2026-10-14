using System.Net;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.OpenDota;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Tests.Infrastructure;

public sealed class OpenDotaParseClientTests
{
    [Fact]
    public async Task AcceptedAndRemovedJob_AreNotConfusedWithParsedMatch()
    {
        var bodies = new Queue<string>(["{\"job\":{\"jobId\":123}}", "{\"jobId\":123}", "null"]);
        var client = Client(new Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(bodies.Dequeue()) }));
        Assert.Equal("123", await client.SubmitAsync(Match(), default));
        Assert.True(await client.IsPendingAsync("123", default));
        Assert.False(await client.IsPendingAsync("123", default));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"job\":{\"jobId\":0}}")]
    [InlineData("{\"job\":{\"jobId\":\"../x\"}}")]
    public async Task InvalidPostResponse_IsAnUnknownOutcome(string body)
    {
        var ex = await Assert.ThrowsAsync<OpenDotaParseException>(() =>
            Client(new Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) }))
                .SubmitAsync(Match(), default));
        Assert.True(ex.OutcomeUnknown);
    }

    [Fact]
    public async Task RateLimitedPost_IsKnownRefusedAndWeighted()
    {
        var limiter = new OpenDotaRateLimiter(Options.Create(new OpenDotaOptions
            { BaseUrl = new("https://example.com/api/"), PermitsPerMinute = 11, MaxQueueWait = TimeSpan.Zero }),
            TimeProvider.System, NullLogger<OpenDotaRateLimiter>.Instance);
        var handler = new OpenDotaRateLimitHandler(limiter, TimeProvider.System, NullLogger<OpenDotaRateLimitHandler>.Instance)
            { InnerHandler = new Stub(_ => new(HttpStatusCode.TooManyRequests)) };
        var ex = await Assert.ThrowsAsync<OpenDotaParseException>(() => Client(handler).SubmitAsync(Match(), default));
        Assert.Equal("rateLimited", ex.Code);
        Assert.False(ex.OutcomeUnknown);
        Assert.True(limiter.IsPaused);
    }

    [Fact]
    public void WeightedReservation_ChargesTenWithoutPartialReservation()
    {
        var limiter = new OpenDotaRateLimiter(Options.Create(new OpenDotaOptions
            { BaseUrl = new("https://example.com/"), PermitsPerMinute = 11, MaxQueueWait = TimeSpan.Zero }),
            TimeProvider.System, NullLogger<OpenDotaRateLimiter>.Instance);
        Assert.Equal(TimeSpan.Zero, limiter.TryReserve(10));
        Assert.Null(limiter.TryReserve(2));
        Assert.Equal(TimeSpan.Zero, limiter.TryReserve());
        Assert.Null(limiter.TryReserve());
    }

    private static MatchId Match() { MatchId.TryCreate(12, out var id); return id; }

    private static OpenDotaParseClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new("https://example.com/api/") });
    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(response(request));
    }
}
