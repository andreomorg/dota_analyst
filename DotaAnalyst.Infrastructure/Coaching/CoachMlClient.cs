using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.Coaching;

public sealed class CoachMlClient(HttpClient client, IOptions<CoachMlOptions> options) : ICoachingReviewProvider
{
    private static readonly JsonSerializerOptions Strict = new(CoachingJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public async Task<CoachingReviewResult> ReviewAsync(CoachingDecisionContext context, CancellationToken cancellationToken)
    {
        var payload = PrepareCoachingReview.Build(context);
        if (payload.Scenes.Count == 0) return new(1, "no-scenes", false, new([]));
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, Strict);
        if (json.Length > 65536) throw new InvalidDataException("Review payload exceeds the service budget.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/reviews") { Content = new ByteArrayContent(json) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CoachingReviewResult>(Strict, cancellationToken)
            ?? throw new InvalidDataException("Missing review report.");
        if (result.SchemaVersion != 1 || string.IsNullOrWhiteSpace(result.Engine) || result.Engine.Length > 120
            || result.HumanReviewed || result.Report is null)
            throw new InvalidDataException("Unsupported review provenance.");
        var errors = CoachingHypothesisValidator.Validate(context, result.Report);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
        return result;
    }
}

public sealed class DisabledCoachingReviewProvider : ICoachingReviewProvider
{
    public Task<CoachingReviewResult> ReviewAsync(CoachingDecisionContext context, CancellationToken cancellationToken)
        => throw new InvalidOperationException("CoachMl is disabled.");
}
