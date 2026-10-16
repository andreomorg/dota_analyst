using System.Text.Json;
using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Application;

public class BoundedCoachingTests
{
    private static CoachingDecisionContext Context() => new(1, "match", 1, new(1, "test", false),
        new(null, "unknown", null, false), "omniscient-retrospective", [],
        [new(new("episode", CoachingEpisodeKind.Death, 1, 40, 25, 55, new(4, 0), null, null, false, null),
            new(30, [new(1, Team.Radiant, 30, 0, 0), new(2, Team.Radiant, 30, 3, 4)], 8, null, null, null, null))]);

    [Fact]
    public void ModelInputHasNoNumericObservationAndKeepsAnchorAndEventSeparate()
    {
        var request = PrepareBoundedCoaching.Build(Context());
        Assert.Equal(3, request.SchemaVersion);
        Assert.Equal("carry", request.Role);
        var scene = Assert.Single(request.Scenes);
        Assert.Equal("available", scene.Availability.AnchorSupportDistance);
        Assert.Equal("none", scene.Availability.EventPositionCoverage);
        Assert.Equal("unknown-death-isolation", scene.RetrospectiveState);
        var json = JsonSerializer.Serialize(request);
        foreach (var field in new[] { "\"Distance\":", "EventClock", "AnchorClock", "Observation", "HeroId", "SampleSeconds" })
            Assert.DoesNotContain(field, json);
        Assert.Equal(2, PrepareGroundedCoaching.Build(Context()).SchemaVersion);
    }

    [Fact]
    public void ValidationRejectsHistoricalVersionAndForeignReferences()
    {
        var request = PrepareBoundedCoaching.Build(Context());
        var response = new GroundedCoachingResponse(3,
            [new("episode", ["outcome"], "Se havia saída, vale comparar rotas.", "A rota pode ter outro custo.",
                "Compare retorno com permanência e confira o trajeto.", "Visão desconhecida.")]);
        Assert.Empty(PrepareBoundedCoaching.Validate(request, response));
        Assert.NotEmpty(PrepareBoundedCoaching.Validate(request, response with { SchemaVersion = 2 }));
        Assert.NotEmpty(PrepareBoundedCoaching.Validate(request, response with
            { Reflections = [response.Reflections[0] with { EvidenceIds = ["position-999"] }] }));
    }
}
