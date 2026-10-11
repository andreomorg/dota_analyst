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
    public async Task GetMatchDetailsAsync_NotFound_ReturnsNullWithoutCaching()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, """{"error":"Not Found"}""");
        var analyzer = CreateAnalyzer(handler);

        Assert.Null(await analyzer.GetMatchDetailsAsync(CreateMatchId(1), CancellationToken.None));
        Assert.Null(await analyzer.GetMatchDetailsAsync(CreateMatchId(1), CancellationToken.None));
        Assert.Equal(2, handler.Calls);
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

    private static OpenDotaReplayAnalyzer CreateAnalyzer(StubHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.opendota.com/api/") };
        return new OpenDotaReplayAnalyzer(
            new OpenDotaClient(httpClient), new MemoryCache(new MemoryCacheOptions()));
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
