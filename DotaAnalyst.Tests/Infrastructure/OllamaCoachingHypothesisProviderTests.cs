using System.Net;
using System.Text;
using System.Text.Json;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Infrastructure.Coaching;

namespace DotaAnalyst.Tests.Infrastructure;

public class OllamaCoachingHypothesisProviderTests
{
    private static CoachingDecisionContext Context() => new(1, "1", 1, new(1, "test", false),
        new(null, "unknown", null, false), "omniscient-retrospective", [],
        [new(new("death", CoachingEpisodeKind.Death, 1, 40, 25, 55, new(4, 0), null, null, false, null),
            new(30, [], 10, null, null, null, null))]);

    private const string Valid = """
        {"assessments":[{"episodeId":"death","disposition":"insufficient","evidenceIds":["outcome"],
        "hypothesis":"Faltam dados para julgar a decisão.","alternative":"Conferir o trecho antes de propor uma alternativa.",
        "exercise":"Rever a informação disponível ao jogador.","limitations":"Sem visibilidade reconstruída."}]}
        """;

    [Fact]
    public async Task ExplicitAbstentionIsAcceptedWithoutClaimingTacticalTruth()
    {
        using var client = Client(Valid);
        var result = await new OllamaCoachingHypothesisProvider(client, "test").GenerateAsync(Context(), CancellationToken.None);
        Assert.Equal("insufficient", Assert.Single(result.Assessments).Disposition);
    }

    [Theory]
    [InlineData("foreign", "outcome")]
    [InlineData("death", "invented-evidence")]
    public async Task ForeignSceneOrEvidenceCannotBeAccepted(string episode, string evidence)
    {
        var content = Valid.Replace("\"episodeId\":\"death\"", $"\"episodeId\":\"{episode}\"")
            .Replace("\"outcome\"", $"\"{evidence}\"");
        using var client = Client(content);
        await Assert.ThrowsAsync<InvalidDataException>(() => new OllamaCoachingHypothesisProvider(client, "test")
            .GenerateAsync(Context(), CancellationToken.None));
    }

    [Fact]
    public async Task FreeExtraFieldsAreRejected()
    {
        using var client = Client(Valid.Replace("{\"assessments\":", "{\"diagnosis\":\"invented\",\"assessments\":"));
        await Assert.ThrowsAsync<JsonException>(() => new OllamaCoachingHypothesisProvider(client, "test")
            .GenerateAsync(Context(), CancellationToken.None));
    }

    [Fact]
    public async Task TruncationCannotBecomePartialAdvice()
    {
        using var client = Client(Valid, "length");
        await Assert.ThrowsAsync<InvalidDataException>(() => new OllamaCoachingHypothesisProvider(client, "test")
            .GenerateAsync(Context(), CancellationToken.None));
    }

    [Fact]
    public async Task FutureSnapshotIsRejectedBeforeNetworkAndEmptyScenesNeedNoModel()
    {
        using var client = new HttpClient(new FailIfCalled()) { BaseAddress = new Uri("http://localhost/") };
        var context = Context();
        var scene = context.Scenes[0];
        scene = scene with { AtOrBeforeAnchor = scene.AtOrBeforeAnchor with { Economy = new(60, 500, 5) } };
        await Assert.ThrowsAsync<InvalidDataException>(() => new OllamaCoachingHypothesisProvider(client, "test")
            .GenerateAsync(context with { Scenes = [scene] }, CancellationToken.None));
        Assert.Empty((await new OllamaCoachingHypothesisProvider(client, "test")
            .GenerateAsync(context with { Scenes = [] }, CancellationToken.None)).Assessments);
    }

    [Fact]
    public void DuplicateScenesInOutputAndOversizedHypothesesAreRejected()
    {
        var report = JsonSerializer.Deserialize<CoachingHypothesisReport>(Valid, OllamaCoachingProvider.JsonOptions)!;
        var context = Context();
        var second = context.Scenes[0] with { RetrospectiveEpisode = context.Scenes[0].RetrospectiveEpisode with { Id = "other" } };
        Assert.NotEmpty(CoachingHypothesisValidator.Validate(context with { Scenes = [context.Scenes[0], second] },
            new([report.Assessments[0], report.Assessments[0]])));
        Assert.NotEmpty(CoachingHypothesisValidator.Validate(Context(), new([report.Assessments[0] with { Hypothesis = new string('x', 701) }])));
    }

    private static HttpClient Client(string content, string reason = "stop") => new(new Reply(content, reason)) { BaseAddress = new Uri("http://localhost/") };
    private sealed class Reply(string content, string reason) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { done = true, done_reason = reason, response = content }), Encoding.UTF8, "application/json")
            });
    }
    private sealed class FailIfCalled : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No network call expected.");
    }
}
