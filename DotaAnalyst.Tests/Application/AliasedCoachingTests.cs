using System.Text.Json;
using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Application;

public class AliasedCoachingTests
{
    private static CoachingDecisionContext Context(bool positions = true) => new(1, "secret-match", 41,
        new(1, "test", false), new(null, "unknown", null, false), "omniscient-retrospective", [],
        [new(new("death-41-40-1", CoachingEpisodeKind.Death, 41, 40, 25, 55, new(4, 0), null, null, false, null),
            new(30, positions ? [new(41, Team.Radiant, 30, 0, 0), new(100, Team.Radiant, 30, 3, 4)] : [],
                positions ? 8 : 10, null, null, null, null))]);
    private static GroundedCoachingReflection Reflection(string scene, params string[] evidence) =>
        new(scene, evidence, "Se havia saída, vale comparar rotas.", "A saída pode ter outro custo.",
            "Compare retornar com permanecer e confira o trajeto.", "Visão desconhecida.");

    [Fact]
    public void SerializationDoesNotExposeReverseMapOrEncodedHeroAndTimeIds()
    {
        var input = PrepareAliasedCoaching.Build(Context());
        var json = JsonSerializer.Serialize(input);
        Assert.Contains("scene-a", json);
        Assert.Contains("anchor-viewer", json);
        Assert.DoesNotContain("death-41-40-1", json);
        Assert.DoesNotContain("position-41", json);
        Assert.DoesNotContain("position-100", json);
        Assert.DoesNotContain("secret-match", json);
    }

    [Fact]
    public void RoundTripRestoresSceneAndEvidenceWithoutChangingText()
    {
        var input = PrepareAliasedCoaching.Build(Context());
        var source = Reflection("scene-a", "outcome", "anchor-viewer", "anchor-ally");
        var restored = Assert.Single(input.Restore(new(3, [source])).Reflections);
        Assert.Equal("death-41-40-1", restored.EpisodeId);
        Assert.Equal(new[] { "outcome", "position-41", "position-100" }, restored.EvidenceIds);
        Assert.Equal(source.Hypothesis, restored.Hypothesis);
    }

    [Theory]
    [InlineData("scene-a", "position-41")]
    [InlineData("death-41-40-1", "outcome")]
    [InlineData("scene-z", "outcome")]
    public void UnknownOrRealIdsAreRejectedBeforeRestoration(string scene, string evidence) =>
        Assert.Throws<InvalidDataException>(() => PrepareAliasedCoaching.Build(Context()).Restore(new(3, [Reflection(scene, evidence)])));

    [Fact]
    public void MissingPositionsDoNotCreatePositionReferences()
    {
        var input = PrepareAliasedCoaching.Build(Context(false));
        Assert.Equal(new[] { "outcome" }, Assert.Single(input.Request.Scenes).EvidenceIds);
        Assert.Throws<InvalidDataException>(() => input.Restore(new(3, [Reflection("scene-a", "anchor-viewer")])));
    }

    [Fact]
    public void MultipleScenesRestoreTheirOwnEvidenceEvenWhenResponseOrderChanges()
    {
        var context = Context();
        var second = context.Scenes[0] with
        { RetrospectiveEpisode = context.Scenes[0].RetrospectiveEpisode with { Id = "other-episode" } };
        var input = PrepareAliasedCoaching.Build(context with { Scenes = [context.Scenes[0], second] });
        var restored = input.Restore(new(3, [Reflection("scene-b", "anchor-ally"), Reflection("scene-a", "outcome")]));
        Assert.Equal("other-episode", restored.Reflections[0].EpisodeId);
        Assert.Equal("death-41-40-1", restored.Reflections[1].EpisodeId);
    }
}
