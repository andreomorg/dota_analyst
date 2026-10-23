using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Domain;

public class CoachingCanonicalSceneTests
{
    private static CoachingDecisionScene Scene(params CoachingPositionSnapshot[] positions) => new(
        new("episode", CoachingEpisodeKind.Death, 1, 835, 820, 850, new(4, 0), null, null, false, null),
        new(825, positions, 10 - positions.Length, null, null, null, null));

    [Fact]
    public void UsesObservedAlliesAndKeepsBothSampleTimesWithoutInferringIsolation()
    {
        var result = CoachingCanonicalSceneBuilder.Build(Scene(
            new(1, Team.Radiant, 825, 0, 0), new(2, Team.Radiant, 823, 3, 4),
            new(3, Team.Dire, 825, 0, 1), new(4, Team.Radiant, 824, 6, 8)));
        Assert.Equal("13:55", result.EventClock);
        Assert.Equal("13:45", result.AnchorClock);
        Assert.Equal("unknown-death-isolation", result.RetrospectiveState);
        Assert.NotNull(result.NearestObservedAlly);
        Assert.Equal(2, result.NearestObservedAlly.AllyHeroId);
        Assert.Equal(825, result.NearestObservedAlly.ViewerSampleSeconds);
        Assert.Equal(823, result.NearestObservedAlly.AllySampleSeconds);
        Assert.Equal(5, result.NearestObservedAlly.Distance);
        Assert.Equal(2, result.NearestObservedAlly.ObservedAllies);
        Assert.Equal(new[] { "position-1", "position-2" }, result.NearestObservedAlly.EvidenceIds);
    }

    [Fact]
    public void MissingViewerOrAlliesDoesNotBecomeZeroDistance()
    {
        Assert.Null(CoachingCanonicalSceneBuilder.Build(Scene(new CoachingPositionSnapshot(2, Team.Radiant, 825, 0, 0))).NearestObservedAlly);
        Assert.Null(CoachingCanonicalSceneBuilder.Build(Scene(new CoachingPositionSnapshot(1, Team.Radiant, 825, 0, 0))).NearestObservedAlly);
    }

    [Theory]
    [InlineData(822)]
    [InlineData(826)]
    [InlineData(-1)]
    public void RejectsSamplesOutsideAnchorWindow(int seconds) =>
        Assert.Throws<ArgumentException>(() => CoachingCanonicalSceneBuilder.Build(Scene(new CoachingPositionSnapshot(1, Team.Radiant, seconds, 0, 0))));

    [Fact]
    public void RejectsDuplicateHeroesAndInvalidAnchor()
    {
        Assert.Throws<ArgumentException>(() => CoachingCanonicalSceneBuilder.Build(Scene(
            new(1, Team.Radiant, 825, 0, 0), new(1, Team.Radiant, 825, 0, 0))));
        var scene = Scene();
        Assert.Throws<ArgumentException>(() => CoachingCanonicalSceneBuilder.Build(scene with
        { AtOrBeforeAnchor = scene.AtOrBeforeAnchor with { AnchorSeconds = 836 } }));
    }

    [Fact]
    public void DistanceDoesNotOverflowAndTiesAreStable()
    {
        var result = CoachingCanonicalSceneBuilder.Build(Scene(
            new(1, Team.Radiant, 825, int.MinValue, 0), new(3, Team.Radiant, 825, int.MaxValue, 0),
            new(2, Team.Radiant, 825, int.MaxValue, 0)));
        Assert.Equal(4294967295d, result.NearestObservedAlly!.Distance);
        Assert.Equal(2, result.NearestObservedAlly.AllyHeroId);
    }

    [Theory]
    [InlineData(true, "near-detected-fight")]
    [InlineData(false, "distant-detected-fight")]
    [InlineData(null, "unknown-fight-proximity")]
    public void FightClassificationComesFromRetrospectiveEvidence(bool? near, string expected)
    {
        var scene = Scene();
        Assert.Equal(expected, CoachingCanonicalSceneBuilder.Build(scene with
        { RetrospectiveEpisode = scene.RetrospectiveEpisode with { Kind = CoachingEpisodeKind.DetectedFight, NearFight = near } }).RetrospectiveState);
    }
}
