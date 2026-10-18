using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Tests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DotaAnalyst.Tests.Server;

public class CoachingIntegrationTests
{
    private const string Key = "test-machine-key-for-coach";
    private static Dictionary<string, string?> Settings => new()
    {
        ["CoachMl:Enabled"] = "true", ["CoachMl:BaseUrl"] = "http://coach.invalid/", ["CoachMl:ApiKey"] = Key
    };

    [Fact]
    public async Task DisabledInternalRoutesReturnNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/internal/coach/contexts/1/1")).StatusCode);
    }

    [Fact]
    public async Task ContextRequiresMachineKeyAndNeverCallsPython()
    {
        using var factory = new ApiFactory(Settings);
        var source = new Source();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ICoachingDecisionSource>(source)));
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/internal/coach/contexts/1/1")).StatusCode);
        Assert.Equal(0, source.Calls);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        var context = await client.GetFromJsonAsync<CoachingReviewRequest>("/api/internal/coach/contexts/1/1");
        Assert.Equal(1, context!.SchemaVersion);
        Assert.Equal("outcome", Assert.Single(Assert.Single(context.Scenes).Evidence).Id);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task ReviewConsumesPythonProviderAndPreservesEngineIdentity()
    {
        using var factory = new ApiFactory(Settings);
        var reviews = new Reviews();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<ICoachingDecisionSource>(new Source());
            s.AddSingleton<ICoachingReviewProvider>(reviews);
        }));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        using var response = await client.PostAsync("/api/internal/coach/reviews/1/1", null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CoachingReviewResult>();
        Assert.Equal("policy-preview-v1", result!.Engine);
        Assert.False(result.HumanReviewed);
        Assert.Equal(1, reviews.Calls);
    }

    private sealed class Source : ICoachingDecisionSource
    {
        public int Calls { get; private set; }
        public Task<CoachingDecisionContext?> FindAsync(MatchId matchId, int heroId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<CoachingDecisionContext?>(CoachMlClientTests.Context());
        }
    }
    private sealed class Reviews : ICoachingReviewProvider
    {
        public int Calls { get; private set; }
        public Task<CoachingReviewResult> ReviewAsync(CoachingDecisionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new CoachingReviewResult(1, "policy-preview-v1", false, new([])));
        }
    }
}
