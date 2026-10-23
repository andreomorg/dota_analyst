using System.Net;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.OpenDota;
using Microsoft.Extensions.Caching.Memory;

namespace DotaAnalyst.Tests.Infrastructure;

public class OpenDotaReplayAnalyzerTests
{
    private static readonly string FixtureJson = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Fixtures", "match-9034320024.json"));

    [Fact]
    public async Task GetMatchDetailsAsync_ExistingMatch_ReturnsAndCachesIt()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, FixtureJson);
        var analyzer = CreateAnalyzer(handler);

        // Act
        var first = await analyzer.GetMatchDetailsAsync(CreateMatchId(9034320024), CancellationToken.None);
        var second = await analyzer.GetMatchDetailsAsync(CreateMatchId(9034320024), CancellationToken.None);

        // Assert
        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("/api/matches/9034320024", handler.LastPath);
    }

    [Fact]
    public async Task GetMatchDetailsAsync_NotFound_ReturnsNullAndCachesIt()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, """{"error":"Not Found"}""");
        var analyzer = CreateAnalyzer(handler);

        Assert.Null(await analyzer.GetMatchDetailsAsync(CreateMatchId(1), CancellationToken.None));
        Assert.Null(await analyzer.GetMatchDetailsAsync(CreateMatchId(1), CancellationToken.None));
        // The second request is answered by the cached "not found", without calling OpenDota.
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "oops")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task GetMatchDetailsAsync_OpenDotaFails_ThrowsDataSourceUnavailable(
        HttpStatusCode statusCode, string body)
    {
        var analyzer = CreateAnalyzer(new StubHandler(statusCode, body));

        await Assert.ThrowsAsync<DataSourceUnavailableException>(
            () => analyzer.GetMatchDetailsAsync(CreateMatchId(1), CancellationToken.None));
    }

    [Fact]
    public async Task GetMatchDetailsAsync_FullCache_StillWorks()
    {
        // A tiny cache: with SizeLimit, entries without a size would throw; extra entries are just evicted.
        var handler = new StubHandler(HttpStatusCode.OK, FixtureJson);
        var analyzer = CreateAnalyzer(handler, sizeLimit: 1);

        foreach (var matchId in new long[] { 1, 2, 3 })
        {
            Assert.NotNull(await analyzer.GetMatchDetailsAsync(CreateMatchId(matchId), CancellationToken.None));
        }

        Assert.Equal(3, handler.Calls);
    }

    // Same SizeLimit setup as production (see AddInfrastructure), so every cache entry must declare its size.
    [Fact]
    public async Task Refresh_BypassesUnparsedCache_AndReplacesCachedResult()
    {
        var handler = new ChangingHandler();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        var analyzer = new OpenDotaReplayAnalyzer(new OpenDotaClient(new HttpClient(handler)
        { BaseAddress = new("https://example.com/api/") }), cache);
        var id = CreateMatchId(12);
        Assert.False((await analyzer.GetMatchDetailsAsync(id, default))!.IsParsed);
        Assert.False((await analyzer.GetMatchDetailsAsync(id, default))!.IsParsed);
        Assert.True((await analyzer.RefreshMatchDetailsAsync(id, default))!.IsParsed);
        Assert.True((await analyzer.GetMatchDetailsAsync(id, default))!.IsParsed);
        Assert.Equal(2, handler.Calls);
    }

    private sealed class ChangingHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                Calls == 1 ? "{\"match_id\":12}" : "{\"match_id\":12,\"version\":21}")
            });
        }
    }

    private static OpenDotaReplayAnalyzer CreateAnalyzer(StubHandler handler, long sizeLimit = 1000)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.opendota.com/api/") };
        return new OpenDotaReplayAnalyzer(
            new OpenDotaClient(httpClient), new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimit }));
    }

    private static MatchId CreateMatchId(long value)
    {
        Assert.True(MatchId.TryCreate(value, out var matchId));
        return matchId;
    }

    // Answers every request with the same response, counting the calls.
    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastPath = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
        }
    }
}
