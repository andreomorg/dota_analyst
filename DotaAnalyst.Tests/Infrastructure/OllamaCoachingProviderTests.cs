using System.Net;
using System.Text;
using System.Text.Json;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Infrastructure.Coaching;

namespace DotaAnalyst.Tests.Infrastructure;

public class OllamaCoachingProviderTests
{
    private static readonly CoachingContext Context = WithFindings(new(2, "1", 1, new(5, "inferred", true),
        new(null, "unknown", null, false), 600, "omniscient-retrospective",
        [new("e1", "Deaths", 3, "count", 0, 600, "replay", "Not a verdict."),
         new("e2", "IsolatedDeaths", 3, "count", 0, 600, "replay", "Sampled.")], []));

    private static CoachingContext WithFindings(CoachingContext context) => context with { Findings = CoachingFindingSelector.Select(context) };

    [Theory]
    [InlineData("length", "{\"selections\":[]}")]
    [InlineData("stop", "{\"selections\":[{\"findingId\":\"fake\",\"exerciseId\":\"fake\"}]}")]
    public async Task TruncatedOrFabricatedAdviceIsRejected(string reason, string content)
    {
        using var client = new HttpClient(new ReplyHandler(reason, content)) { BaseAddress = new Uri("http://localhost/") };
        await Assert.ThrowsAsync<InvalidDataException>(() => new OllamaCoachingProvider(client, "test").GenerateAsync(Context, CancellationToken.None));
    }

    [Fact]
    public async Task ModelCanAbstainWithoutBreakingTheFlow()
    {
        using var client = new HttpClient(new ReplyHandler("stop", "{\"selections\":[]}")) { BaseAddress = new Uri("http://localhost/") };
        var advice = await new OllamaCoachingProvider(client, "test").GenerateAsync(Context, CancellationToken.None);
        Assert.Empty(advice.Suggestions);
    }

    [Fact]
    public async Task FreeTextCannotOverrideACanonicalObservation()
    {
        var content = """{"selections":[{"findingId":"review-isolated-deaths","exerciseId":"review-isolated-deaths-role-5","interpretation":"No assists"}]}""";
        using var client = new HttpClient(new ReplyHandler("stop", content)) { BaseAddress = new Uri("http://localhost/") };
        await Assert.ThrowsAsync<JsonException>(() => new OllamaCoachingProvider(client, "test").GenerateAsync(Context, CancellationToken.None));
    }

    [Fact]
    public async Task ValidChoiceProducesOnlyBackendAuthoredText()
    {
        var content = """{"selections":[{"findingId":"review-isolated-deaths","exerciseId":"review-isolated-deaths-role-5"}]}""";
        using var client = new HttpClient(new ReplyHandler("stop", content)) { BaseAddress = new Uri("http://localhost/") };
        var advice = await new OllamaCoachingProvider(client, "test").GenerateAsync(Context, CancellationToken.None);
        var suggestion = Assert.Single(advice.Suggestions);
        Assert.Equal("review-isolated-deaths", suggestion.FindingId);
        Assert.Equal(new[] { "e1", "e2" }, suggestion.EvidenceIds);
    }

    [Fact]
    public async Task NoCandidatesDoesNotCallTheModel()
    {
        using var client = new HttpClient(new FailIfCalled()) { BaseAddress = new Uri("http://localhost/") };
        var context = Context with { Evidence = [], Findings = [] };
        var provider = new OllamaCoachingProvider(client, "test");
        Assert.Empty((await provider.GenerateAsync(context, CancellationToken.None)).Suggestions);
        Assert.Null(provider.LastMetrics);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.GenerateAsync(Context with { SchemaVersion = 1 }, CancellationToken.None));
    }

    private sealed class FailIfCalled : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No network call expected.");
    }

    private sealed class ReplyHandler(string reason, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { done = true, done_reason = reason, response = content }), Encoding.UTF8, "application/json")
            });
    }
}
