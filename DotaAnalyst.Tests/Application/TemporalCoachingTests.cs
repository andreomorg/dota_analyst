using System.Text.Json;
using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Application;

public class TemporalCoachingTests
{
    private static CoachingDecisionContext Context() => new(1, "private-match", 41, new(1, "test", false),
        new(null, "unknown", null, false), "omniscient-retrospective", [],
        [new(new("death-41-40", CoachingEpisodeKind.Death, 41, 40, 25, 55, new(4, 0), null, null, false, null),
            new(30, [new(41, Team.Radiant, 30, 0, 0), new(100, Team.Radiant, 30, 30, 0)], 8, null, null, null, null))]);

    [Fact]
    public void NumericProvenanceStaysOutOfModelInputWhileChangeHasItsOwnReference()
    {
        var input = PrepareTemporalCoaching.Build(Context(),
            [new(41, [new(20, 0, 0)]), new(100, [new(20, 10, 0)])]);
        var scene = Assert.Single(input.Request.Scenes);
        Assert.Equal(4, input.Request.SchemaVersion);
        Assert.Equal("recorded-separation-increased", scene.SupportTransition.Change);
        Assert.Contains("support-transition", scene.EvidenceIds);
        Assert.Equal(10, Assert.Single(input.GetProvenance()).Earlier!.Distance);
        var json = JsonSerializer.Serialize(input);
        Assert.DoesNotContain("death-41-40", json);
        Assert.DoesNotContain("position-100", json);
        Assert.DoesNotContain("EarlierTargetSeconds", json);
        Assert.DoesNotContain("private-match", json);
    }

    [Fact]
    public void KnownAnchorDistanceAndUnknownTransitionRemainIndependent()
    {
        var input = PrepareTemporalCoaching.Build(Context(), null);
        var scene = Assert.Single(input.Request.Scenes);
        Assert.Equal("available", scene.Availability.AnchorSupportDistance);
        Assert.Equal("unavailable", scene.SupportTransition.Change);
        Assert.DoesNotContain("support-transition", scene.EvidenceIds);
        var invalid = new GroundedCoachingResponse(4, [Reflection("support-transition")]);
        Assert.Throws<InvalidDataException>(() => input.Restore(invalid));
    }

    [Fact]
    public void NewEvidenceRestoresToItsOwnCanonicalObservationAndRejectsOldVersion()
    {
        var input = PrepareTemporalCoaching.Build(Context(),
            [new(41, [new(20, 0, 0)]), new(100, [new(20, 10, 0)])]);
        var report = input.Restore(new(4, [Reflection("support-transition")]));
        Assert.Equal("death-41-40", report.Reflections[0].EpisodeId);
        Assert.Equal("support-transition:death-41-40", report.Reflections[0].EvidenceIds[0]);
        Assert.Throws<InvalidDataException>(() => input.Restore(new(3, [Reflection("support-transition")])));
    }

    private static GroundedCoachingReflection Reflection(string evidence) => new("scene-a", [evidence],
        "Se a mudança alterar as opções, vale comparar os trajetos.", "O aliado pode ter se deslocado por outra tarefa.",
        "Compare manter a rota com acompanhar apoio e confira quem mudou de posição.", "Intenção e viabilidade não reconstruídas.");
}
