using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using static DotaAnalyst.Tests.Domain.ReplayBuilder;

namespace DotaAnalyst.Tests.Domain;

public class ReplayAnalysisTests
{
    [Fact]
    public void IsOutdated_WhenMovementMetricsAreMissing()
    {
        var metrics = new HeroMetrics(
            31, Team.Radiant, 1, 0, 0, 0, 0, 0, 1000, null, 10, 0.5, 0, 0, null, 0, 0,
            PerRegion<double>.Of(_ => 0.125), PerRegion<int>.Of(_ => 0));
        var replay = new ReplayAnalysis(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds, [Hero(31, Team.Radiant, Lane.Bottom)],
            timeline: Timeline(Hero(31, Team.Radiant, Lane.Bottom)), deaths: [], wards: [], damage: [], purchases: [],
            objectives: [], metrics: [metrics]);

        Assert.True(replay.IsOutdated);

        var currentMetrics = metrics with
        {
            EstimatedMovementDistance = 0,
            EstimatedMovementByRegion = PerRegion<double>.Of(_ => 0),
            MovementSampleSegments = 0,
            TimeByRegionByPhase = new PerGamePhase<PerRegion<double>>(
                PerRegion<double>.Of(_ => 0), PerRegion<double>.Of(_ => 0), PerRegion<double>.Of(_ => 0)),
            DeathsByRegionByPhase = new PerGamePhase<PerRegion<int>>(
                PerRegion<int>.Of(_ => 0), PerRegion<int>.Of(_ => 0), PerRegion<int>.Of(_ => 0)),
            TeamFights = 0,
            FightsNearby = 0,
            QuickRedeaths = 0,
            LaneDamageTaken = 0,
            TeamObserverUptime = 0,
            MinutesByRegion = PerRegion<int>.Of(_ => 0),
        };
        var currentReplay = new ReplayAnalysis(
            MatchIdOf(10), ReplayAnalysis.LaningPhaseEndsAtSeconds, [Hero(31, Team.Radiant, Lane.Bottom)],
            timeline: Timeline(Hero(31, Team.Radiant, Lane.Bottom)), deaths: [], wards: [], damage: [], purchases: [],
            objectives: [], metrics: [currentMetrics]);

        Assert.False(currentReplay.IsOutdated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(420)] // the match ended before 10:00
    [InlineData(ReplayAnalysis.LaningPhaseEndsAtSeconds)]
    public void Create_AcceptsTimesUpToTheEndOfTheLaningPhase(int endsAtSeconds)
    {
        var replay = new ReplayAnalysis(MatchIdOf(10), endsAtSeconds, [Hero(31, Team.Radiant, Lane.Bottom)]);

        Assert.Equal(endsAtSeconds, replay.EndsAtSeconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ReplayAnalysis.LaningPhaseEndsAtSeconds + 1)]
    public void Create_RejectsTimesOutsideTheLaningPhase(int endsAtSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayAnalysis(MatchIdOf(10), endsAtSeconds, []));
    }

    [Fact]
    public void Create_RejectsAnInvalidMatchId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayAnalysis(default, 600, []));
    }

    [Fact]
    public void Create_RejectsTheSameHeroTwice()
    {
        // Players are matched by hero ID, so it must be unique.
        Assert.Throws<ArgumentException>(() => Analysis(
            10, Hero(31, Team.Radiant, Lane.Bottom), Hero(31, Team.Dire, Lane.Top)));
    }

    [Fact]
    public void Create_KeepsItsOwnCopyOfThePlayers()
    {
        var players = new List<LaningStats> { Hero(31, Team.Radiant, Lane.Bottom) };
        var replay = new ReplayAnalysis(MatchIdOf(10), 600, players);

        players.Add(Hero(1, Team.Dire, Lane.Top));

        Assert.Single(replay.Players);
    }

    [Fact]
    public void FindHero_FindsByHeroId()
    {
        var replay = Analysis(10, Hero(31, Team.Radiant, Lane.Bottom), Hero(1, Team.Dire, Lane.Top));

        Assert.Equal(Team.Dire, replay.FindHero(1)?.Team);
        Assert.Null(replay.FindHero(2));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]    // hero ID must be positive
    [InlineData(31, -1, 0, 0, 0, 0)]
    [InlineData(31, 0, -1, 0, 0, 0)]
    [InlineData(31, 0, 0, -1, 0, 0)]
    [InlineData(31, 0, 0, 0, -1, 0)]
    [InlineData(31, 0, 0, 0, 0, -1)]
    public void LaningStats_RejectsInvalidNumbers(int heroId, int lastHits, int denies, int netWorth, int experience, int deaths)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LaningStats(heroId, Team.Radiant, Lane.Top, lastHits, denies, netWorth, experience, deaths));
    }
}
