using System.Net;
using System.Text;
using System.Text.Json;
using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Infrastructure.Coaching;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Tests.Infrastructure;

public class CoachMlClientTests
{
    internal static CoachingDecisionContext Context() => new(1, "1", 1, new(1, "test", false),
        new(null, "unknown", null, false), "omniscient-retrospective", [],
        [new(new("death", CoachingEpisodeKind.Death, 1, 40, 25, 55, new(4, 0), null, null, false, null),
            new(30, [], 10, null, null, null, null))]);

    private const string Valid = """
        {"schemaVersion":1,"engine":"policy-preview-v1","humanReviewed":false,"report":{"assessments":[
        {"episodeId":"death","disposition":"insufficient","evidenceIds":["outcome"],
        "hypothesis":"Faltam dados para julgar a decisão.","alternative":"Conferir o trecho.",
        "exercise":"Rever a informação disponível ao jogador.","limitations":"Sem visibilidade reconstruída."}]}}
        """;

    private static CoachMlClient Provider(HttpClient client) => new(client,
        Options.Create(new CoachMlOptions { ApiKey = "test-key-for-service" }));

    [Fact]
    public async Task SendsEvidenceToPythonWithAuthenticationAndPreservesProvenance()
    {
        using var client = new HttpClient(new Reply(Valid, request =>
        {
            Assert.Equal("http://localhost/v1/reviews", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer test-key-for-service", request.Headers.Authorization!.ToString());
        })) { BaseAddress = new("http://localhost/") };
        var result = await Provider(client).ReviewAsync(Context(), CancellationToken.None);
        Assert.Equal("policy-preview-v1", result.Engine);
        Assert.False(result.HumanReviewed);
        Assert.Equal("insufficient", Assert.Single(result.Report.Assessments).Disposition);
    }

    [Theory]
    [InlineData("foreign", "outcome")]
    [InlineData("death", "invented")]
    public async Task UnknownScenesAndReferencesAreRejected(string episode, string evidence)
    {
        using var client = Client(Valid.Replace("\"episodeId\":\"death\"", $"\"episodeId\":\"{episode}\"")
            .Replace("\"outcome\"", $"\"{evidence}\""));
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(client).ReviewAsync(Context(), CancellationToken.None));
    }

    [Fact]
    public async Task ExtraFieldsAndFakeHumanApprovalAreRejected()
    {
        using var extra = Client(Valid.Replace("{\"schemaVersion\"", "{\"invented\":true,\"schemaVersion\""));
        await Assert.ThrowsAsync<JsonException>(() => Provider(extra).ReviewAsync(Context(), CancellationToken.None));
        using var approval = Client(Valid.Replace("\"humanReviewed\":false", "\"humanReviewed\":true"));
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(approval).ReviewAsync(Context(), CancellationToken.None));
    }

    [Fact]
    public async Task FutureSnapshotsAreRejectedBeforeNetworkAndEmptyScenesNeedNoService()
    {
        using var client = new HttpClient(new FailIfCalled()) { BaseAddress = new("http://localhost/") };
        var context = Context();
        var scene = context.Scenes[0];
        scene = scene with { AtOrBeforeAnchor = scene.AtOrBeforeAnchor with { Economy = new(60, 500, 5) } };
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(client).ReviewAsync(context with { Scenes = [scene] }, CancellationToken.None));
        Assert.Empty((await Provider(client).ReviewAsync(context with { Scenes = [] }, CancellationToken.None)).Report.Assessments);
    }

    [Fact]
    public async Task ServiceErrorsDoNotBecomeAdvice()
    {
        using var client = new HttpClient(new Unavailable()) { BaseAddress = new("http://localhost/") };
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(client).ReviewAsync(Context(), CancellationToken.None));
    }

    [Fact]
    public void DuplicateScenesAndOversizedTextRemainInvalid()
    {
        var result = JsonSerializer.Deserialize<CoachingReviewResult>(Valid, CoachingJson.Options)!;
        var context = Context();
        var row = result.Report.Assessments[0];
        var other = context.Scenes[0] with { RetrospectiveEpisode = context.Scenes[0].RetrospectiveEpisode with { Id = "other" } };
        Assert.NotEmpty(CoachingHypothesisValidator.Validate(context with { Scenes = [context.Scenes[0], other] }, new([row, row])));
        Assert.NotEmpty(CoachingHypothesisValidator.Validate(context, new([row with { Hypothesis = new string('x', 701) }])));
    }

    private static HttpClient Client(string body) => new(new Reply(body)) { BaseAddress = new("http://localhost/") };
    private sealed class Reply(string body, Action<HttpRequestMessage>? inspect = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            inspect?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class FailIfCalled : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No network expected.");
    }
    private sealed class Unavailable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
