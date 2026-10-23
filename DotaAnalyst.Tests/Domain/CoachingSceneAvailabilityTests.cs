using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Domain;

public class CoachingSceneAvailabilityTests
{
    private static CoachingDecisionScene Scene() => new(
        new("death", CoachingEpisodeKind.Death, 1, 40, 25, 55, new(4, 0), null, null, false, null),
        new(30, [new(1, Team.Radiant, 30, 0, 0), new(2, Team.Radiant, 29, 3, 4)], 8, null, null, null, null));

    [Fact]
    public void AvailableEarlierDistanceDoesNotFillMissingEventCoverage()
    {
        var result = CoachingSceneAvailabilityBuilder.Build(Scene());
        Assert.Equal("available", result.AnchorSupportDistance);
        Assert.Equal("different-recorded-seconds", result.AnchorSampleSynchronization);
        Assert.Equal("none", result.EventPositionCoverage);
    }

    [Fact]
    public void MissingViewerDoesNotBecomeUnknownEventWhenEventIsCovered()
    {
        var scene = Scene();
        var result = CoachingSceneAvailabilityBuilder.Build(scene with
        {
            RetrospectiveEpisode = scene.RetrospectiveEpisode with { PositionCoverage = new(4, 4), IsolatedDeath = true },
            AtOrBeforeAnchor = scene.AtOrBeforeAnchor with { Positions = [new(2, Team.Radiant, 30, 0, 0)] }
        });
        Assert.Equal(new CoachingSceneAvailability("missing-viewer-sample", "not-comparable", "complete"), result);
    }

    [Fact]
    public void ViewerWithoutAllyIsDistinctFromMissingViewer()
    {
        var scene = Scene();
        Assert.Equal("no-observed-ally", CoachingSceneAvailabilityBuilder.Build(scene with
        { AtOrBeforeAnchor = scene.AtOrBeforeAnchor with { Positions = [new(1, Team.Radiant, 30, 0, 0)] } }).AnchorSupportDistance);
    }

    [Fact]
    public void RejectsImpossibleCoverage()
    {
        var scene = Scene();
        Assert.Throws<ArgumentException>(() => CoachingSceneAvailabilityBuilder.Build(scene with
        { RetrospectiveEpisode = scene.RetrospectiveEpisode with { PositionCoverage = new(4, 5) } }));
    }
}
