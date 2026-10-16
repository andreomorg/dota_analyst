using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class CoachingSupportTransitionTests
{
    private static CoachingDecisionScene Scene() => new(
        new("death", CoachingEpisodeKind.Death, 1, 40, 25, 55, new(4, 0), null, null, false, null),
        new(30, [new(1, Team.Radiant, 30, 0, 0), new(2, Team.Radiant, 30, 30, 0), new(3, Team.Radiant, 30, 100, 0)],
            7, null, null, null, null));

    [Fact]
    public void ComparesTheSameAllyEvenWhenEarlierNearestWasAnotherHero()
    {
        var result = CoachingSupportTransitionBuilder.Build(Scene(),
            [new(1, [new(20, 0, 0), new(30, 0, 0)]), new(2, [new(20, 10, 0), new(30, 30, 0)]),
             new(3, [new(20, 1, 0), new(30, 100, 0)])]);
        Assert.Equal(2, result.AllyHeroId);
        Assert.Equal(10, result.Earlier!.Distance);
        Assert.Equal(30, result.AtAnchor!.Distance);
        Assert.Equal(CoachingSupportChange.Increased, result.Change);
        Assert.Equal(2, result.ObservedPairedSeconds);
        Assert.Equal(11, result.ExpectedPairedSeconds);
    }

    [Theory]
    [InlineData(18, true)]
    [InlineData(17, false)]
    [InlineData(21, false)]
    public void EarlierSampleMustBeAtOrBeforeTargetAndWithinAge(int seconds, bool available)
    {
        var result = CoachingSupportTransitionBuilder.Build(Scene(),
            [new(1, [new(seconds, 0, 0), new(30, 0, 0)]), new(2, [new(20, 10, 0), new(30, 30, 0)])]);
        Assert.Equal(available, result.Earlier is not null);
        if (available) Assert.Equal(seconds, result.Earlier!.ViewerSampleSeconds);
        else Assert.Equal("earlier-viewer-unobserved", result.UnavailableReason);
        Assert.NotNull(result.AtAnchor);
    }

    [Fact]
    public void DecreasingAndUnchangedDistanceKeepTheirObservedDirection()
    {
        var decreasing = CoachingSupportTransitionBuilder.Build(Scene(),
            [new(1, [new(20, 0, 0)]), new(2, [new(20, 40, 0)])]);
        var unchanged = CoachingSupportTransitionBuilder.Build(Scene(),
            [new(1, [new(20, 0, 0)]), new(2, [new(20, 30, 0)])]);
        Assert.Equal(CoachingSupportChange.Decreased, decreasing.Change);
        Assert.Equal(CoachingSupportChange.Unchanged, unchanged.Change);
    }

    [Fact]
    public void MissingAnchorDoesNotBecomeDistanceFromAnEarlierSample()
    {
        var scene = Scene();
        var result = CoachingSupportTransitionBuilder.Build(scene with
            { AtOrBeforeAnchor = scene.AtOrBeforeAnchor with { Positions = [] } },
            [new(1, [new(20, 0, 0)]), new(2, [new(20, 10, 0)])]);
        Assert.Null(result.AtAnchor);
        Assert.Equal(CoachingSupportChange.Unavailable, result.Change);
        Assert.Equal("anchor-viewer-unobserved", result.UnavailableReason);
    }

    [Fact]
    public void EarlyAnchorCannotInventAnEarlierWindow()
    {
        var scene = Scene();
        var result = CoachingSupportTransitionBuilder.Build(scene with
            { AtOrBeforeAnchor = scene.AtOrBeforeAnchor with { AnchorSeconds = 5, Positions = [] } }, null);
        Assert.Null(result.EarlierTargetSeconds);
        Assert.Equal(0, result.ExpectedPairedSeconds);
        Assert.Equal("insufficient-earlier-window", result.UnavailableReason);
    }

    [Fact]
    public void LargeEndpointJumpDoesNotFillIntermediateSamples()
    {
        var scene = Scene();
        var result = CoachingSupportTransitionBuilder.Build(scene with
            { AtOrBeforeAnchor = scene.AtOrBeforeAnchor with
                { Positions = [new(1, Team.Radiant, 30, 0, 0), new(2, Team.Radiant, 30, 10000, 0)] } },
            [new(1, [new(20, 0, 0), new(30, 0, 0)]), new(2, [new(20, 10, 0), new(30, 10000, 0)])]);
        Assert.Equal(CoachingSupportChange.Increased, result.Change);
        Assert.Equal(10000, result.AtAnchor!.Distance);
        Assert.Equal(2, result.ObservedPairedSeconds); // No interpolation or claim about walking speed.
    }

    [Fact]
    public void EveryExactPairIsCountedButOutcomeDoesNotAffectEarlierFact()
    {
        var tracks = new HeroPositions[]
        {
            new(1, Enumerable.Range(20, 11).Select(s => new PositionSample(s, 0, 0)).ToArray()),
            new(2, Enumerable.Range(20, 11).Select(s => new PositionSample(s, s, 0)).ToArray())
        };
        var scene = Scene();
        var result = CoachingSupportTransitionBuilder.Build(scene, tracks);
        Assert.Equal(11, result.ObservedPairedSeconds);
        Assert.Equal(result, CoachingSupportTransitionBuilder.Build(scene with
            { RetrospectiveEpisode = scene.RetrospectiveEpisode with { PositionCoverage = new(4, 4), IsolatedDeath = true } }, tracks));
    }

    [Fact]
    public void DuplicateTracksAreRejected()
    {
        Assert.Throws<ArgumentException>(() => CoachingSupportTransitionBuilder.Build(Scene(),
            [new(1, [new(20, 0, 0)]), new(1, [new(20, 1, 1)])]));
    }
}
