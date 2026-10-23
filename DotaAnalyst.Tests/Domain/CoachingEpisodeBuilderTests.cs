using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Tests.Domain;

public class CoachingEpisodeBuilderTests
{
    private static readonly int[] LastHitValues = [0, 5, 7];

    private static HeroDeath Death(int hero, int second, bool reincarnated = false) =>
        new(hero, second, 0, 0, KillerKind.Other, null, reincarnated);

    private static ReplayAnalysis Replay(params HeroDeath[] deaths)
    {
        MatchId.TryCreate(1, out var id);
        var players = Enumerable.Range(1, 10).Select(hero => new LaningStats(hero,
            hero <= 5 ? Team.Radiant : Team.Dire, Lane.Middle, 0, 0, 0, 0, 0)).ToArray();
        var timeline = new MatchTimeline([0, 60, 120],
            [new HeroTimeline(1, Team.Radiant, [100, 200, 150], [0, 0, 0],
                new Dictionary<StatKey, IReadOnlyList<int>> { [StatKey.LastHits] = LastHitValues })]);
        return new(id, 120, players, timeline, deaths: deaths);
    }

    [Fact]
    public void MissingAlliesAreUnknownRatherThanIsolated()
    {
        var episode = Assert.Single(CoachingEpisodeBuilder.Build(Replay(Death(1, 30)), 1, null));
        Assert.Null(episode.IsolatedDeath);
        Assert.Equal(new CoachingPositionCoverage(4, 0), episode.PositionCoverage);
        Assert.Null(episode.NearFight);
    }

    [Fact]
    public void FullAllyCoverageCanConfirmSampledIsolation()
    {
        var positions = Enumerable.Range(2, 4).Select(hero => new HeroPositions(hero, [new(30, 5000, 5000)])).ToArray();
        var episode = Assert.Single(CoachingEpisodeBuilder.Build(Replay(Death(1, 30)), 1, positions));
        Assert.True(episode.IsolatedDeath);
        Assert.True(episode.PositionCoverage.IsComplete);
    }

    [Fact]
    public void OneNearbyAllyRulesOutIsolationEvenWithPartialCoverage()
    {
        var episode = Assert.Single(CoachingEpisodeBuilder.Build(Replay(Death(1, 30)), 1,
            [new HeroPositions(2, [new(31, 100, 100)])]));
        Assert.False(episode.IsolatedDeath);
        Assert.False(episode.PositionCoverage.IsComplete);
    }

    [Fact]
    public void MissingFightPositionsDoNotProveDistance()
    {
        var episodes = CoachingEpisodeBuilder.Build(Replay(Death(6, 60), Death(7, 65), Death(8, 70)), 1, null);
        var fight = Assert.Single(episodes);
        Assert.Equal(CoachingEpisodeKind.DetectedFight, fight.Kind);
        Assert.Null(fight.NearFight);
        Assert.Equal(55, fight.FromSeconds);
        Assert.Equal(70, fight.ToSeconds);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5000, false)]
    public void FightProximityUsesExistingRadiusAndWindow(int coordinate, bool near)
    {
        var samples = Enumerable.Range(27, 9).Select(i => new PositionSample(i * 2, coordinate, coordinate)).ToArray();
        var fight = Assert.Single(CoachingEpisodeBuilder.Build(Replay(Death(6, 60), Death(7, 65), Death(8, 70)), 1,
            [new HeroPositions(1, samples)]));
        Assert.True(fight.PositionCoverage.IsComplete);
        Assert.Equal(near, fight.NearFight);
    }

    [Fact]
    public void OneNearbySampleConfirmsOnlyProximityNotCompleteCoverage()
    {
        var fight = Assert.Single(CoachingEpisodeBuilder.Build(Replay(Death(6, 60), Death(7, 65), Death(8, 70)), 1,
            [new HeroPositions(1, [new(60, 0, 0)])]));
        Assert.True(fight.NearFight);
        Assert.False(fight.PositionCoverage.IsComplete);
    }

    [Fact]
    public void BoundariesReincarnationAndEconomicIntervalRemainExplicit()
    {
        var episodes = CoachingEpisodeBuilder.Build(Replay(Death(1, -1), Death(1, 0, true), Death(1, 90), Death(1, 120), Death(1, 121)), 1, null);
        Assert.Equal(3, episodes.Count);
        Assert.Equal(0, episodes[0].FromSeconds);
        Assert.True(episodes[0].Reincarnated);
        Assert.Equal(new CoachingEconomyInterval(60, 120, -50, 2), episodes[1].Economy);
        Assert.Equal(120, episodes[2].ToSeconds);
        Assert.Null(episodes[2].Economy);
        Assert.Equal(episodes.Select(e => e.Id).Distinct().Count(), episodes.Count);
    }
}
