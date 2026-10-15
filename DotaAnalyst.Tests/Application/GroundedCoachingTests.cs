using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Tests.Application;

public class GroundedCoachingTests
{
    private static CoachingDecisionContext Context() => new(1, "match", 1, new(1, "test", false),
        new(null, "unknown", null, false), "omniscient-retrospective", [],
        [new(new("episode", CoachingEpisodeKind.Death, 1, 835, 820, 850, new(4, 0), null, null, false, null),
            new(825, [], 10, null, null, null, null))]);

    [Fact]
    public void ObservationOwnsTimeAndUncertaintyAndPreservesHistoricalInput()
    {
        var context = Context();
        var request = PrepareGroundedCoaching.Build(context);
        Assert.Equal(2, request.SchemaVersion);
        var scene = Assert.Single(request.Scenes);
        Assert.Contains("13:55", scene.Observation);
        Assert.Contains("13:45", scene.Observation);
        Assert.Contains("isolamento desconhecido", scene.Observation);
        Assert.Contains("Não há posições suficientes", scene.Observation);
        Assert.Equal(1, context.SchemaVersion);
        Assert.Null(context.Scenes[0].RetrospectiveEpisode.IsolatedDeath);
    }

    [Fact]
    public void RejectsForeignHeroEmptyIdentityAndRepeatedScenes()
    {
        var context = Context();
        Assert.Throws<ArgumentException>(() => PrepareGroundedCoaching.Build(context with { HeroId = 2 }));
        Assert.Throws<ArgumentException>(() => PrepareGroundedCoaching.Build(context with
            { Scenes = [context.Scenes[0], context.Scenes[0]] }));
        Assert.Throws<ArgumentException>(() => PrepareGroundedCoaching.Build(context with
            { Scenes = [context.Scenes[0] with { RetrospectiveEpisode = context.Scenes[0].RetrospectiveEpisode with { Id = "" } }] }));
    }

    [Fact]
    public void ReflectionCannotReplaceObservationOrIntroduceClassificationFields()
    {
        var request = PrepareGroundedCoaching.Build(Context());
        var reflection = new GroundedCoachingReflection("episode", ["outcome"],
            "Se havia uma rota segura, vale conferir a escolha.", "A posição pode ter atendido outro objetivo.",
            "Pause na âncora e compare duas rotas.", "Visão e intenção não reconstruídas.");
        Assert.Empty(PrepareGroundedCoaching.Validate(request, new(2, [reflection])));
        Assert.NotEmpty(PrepareGroundedCoaching.Validate(request, new(1, [reflection])));
        Assert.NotEmpty(PrepareGroundedCoaching.Validate(request, new(2, [reflection with { EvidenceIds = ["position-99"] }])));
        Assert.NotEmpty(PrepareGroundedCoaching.Validate(request, new(2, [reflection with { EvidenceIds = ["outcome", "outcome"] }])));
        Assert.NotEmpty(PrepareGroundedCoaching.Validate(request, new(2, [])));
    }
}
