using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

// MapObjectiveMetrics: the hero's objective counters and its presence when the team took Roshan, Tormentors and towers.
public class MapObjectiveMetricsTests
{
    private static readonly MapObjective Roshan = new(ObjectiveKind.Roshan, 1200, Team.Radiant, killerHeroId: 1);
    private static readonly MapObjective OtherRoshan = new(ObjectiveKind.Roshan, 2400, Team.Radiant, killerHeroId: 1);
    private static readonly MapObjective EnemyRoshan = new(ObjectiveKind.Roshan, 1800, Team.Dire, killerHeroId: 6);
    private static readonly MapObjective Tower = new(ObjectiveKind.Tower, 600, Team.Radiant, killerHeroId: null,
        owner: Team.Dire, towerTier: 1, lane: Lane.Middle);

    private static HeroPositions At(params (int Seconds, int X, int Y)[] samples) =>
        new(1, samples.Select(sample => new PositionSample(sample.Seconds, sample.X, sample.Y)).ToList());

    [Fact]
    public void Presence_CountsTheTeamsObjectivesTheHeroWasNear()
    {
        // Near Roshan (1,000 away, a sample 2 s after the kill), far from the tower.
        var positions = At((1202, 3000, -2000), (600, -5000, -5000));
        IReadOnlyList<ObjectivePlace> places = [new(Roshan, 3000, -3000), new(EnemyRoshan, 3000, -3000), new(Tower, 500, 500)];

        var metrics = MapObjectiveMetricsCalculator.Calculate(Team.Radiant, positions, [Roshan, EnemyRoshan, Tower], places, null);

        Assert.Equal(1, metrics.RoshanKills);   // the enemy's Roshan is not the team's
        Assert.Equal(1, metrics.RoshanKillsNearby);
        Assert.Equal(1, metrics.EnemyTowersTaken);
        Assert.Equal(0, metrics.EnemyTowersTakenNearby);
        Assert.Null(metrics.TormentorKillsNearby);   // none taken
        Assert.Null(metrics.Counts);
    }

    [Fact]
    public void Presence_IsUnknownWhenOnePlaceIsMissing()
    {
        IReadOnlyList<ObjectivePlace> places = [new(Roshan, 3000, -3000)];

        var metrics = MapObjectiveMetricsCalculator.Calculate(Team.Radiant, At((1200, 3000, -3000)), [Roshan, OtherRoshan], places, null);

        Assert.Equal(2, metrics.RoshanKills);
        Assert.Null(metrics.RoshanKillsNearby);
    }

    [Fact]
    public void Shrines_LostOnOwnSideAndTakenOnTheEnemys_AreTeamCounts()
    {
        IReadOnlyList<WisdomTake> wisdom =
        [
            new(6, Team.Dire, 420, Team.Radiant),     // the enemy took the Radiant shrine
            new(2, Team.Radiant, 420, Team.Radiant),  // own shrine kept
            new(3, Team.Radiant, 840, Team.Dire),     // the Dire shrine stolen
        ];

        var metrics = MapObjectiveMetricsCalculator.Calculate(Team.Radiant, null, [], null, null, wisdom);
        var unknown = MapObjectiveMetricsCalculator.Calculate(Team.Radiant, null, [], null, null, [.. wisdom, new(4, Team.Radiant, 1260, null)]);

        Assert.Equal(1, metrics.OwnShrinesLost);
        Assert.Equal(1, metrics.EnemyShrinesTaken);
        Assert.Null(unknown.OwnShrinesLost);
    }

    [Fact]
    public void Comparer_ReadsCountersPer30AndPresenceShares()
    {
        var counts = new MapObjectiveCounts(4, 2, 1, 3, 6, 5, 1, 0, 2);
        var hero = new HeroMetrics(1, Team.Radiant, 2, 3, 1, 1, 0.2, 0.1, 4000, 9000, 50, 0.3, 0, 0, null, 0, 0,
            Objectives: new MapObjectiveMetrics(counts, 2, 1, 0, null, 4, 3));

        var distributions = BaselineComparer.Distribute(
            Enumerable.Repeat(new BaselineSample(hero, 1800), BaselineComparer.MinSampleSize).ToList());

        Assert.Equal(6, distributions.Single(d => d.Metric == ComparedMetric.LotusesPer30).Quantiles[10]);
        Assert.Equal(0.5, distributions.Single(d => d.Metric == ComparedMetric.RoshanPresence).Quantiles[10]);
        Assert.Equal(0.75, distributions.Single(d => d.Metric == ComparedMetric.TowerPresence).Quantiles[10]);
        Assert.DoesNotContain(distributions, d => d.Metric == ComparedMetric.TormentorPresence);
    }
}
