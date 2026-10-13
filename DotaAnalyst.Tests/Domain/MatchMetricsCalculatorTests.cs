using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class MatchMetricsCalculatorTests
{
    // Radiant: heroes 1 (role 1) and 2 (role 5); Dire: hero 6 (role 1). Radiant's side is x + y < 0.
    private static readonly LaningStats[] Players =
    [
        new(1, Team.Radiant, Lane.Bottom, lastHits: 50, 2, 4000, 3000, 0),
        new(2, Team.Radiant, Lane.Bottom, lastHits: 5, 1, 2000, 2500, 0),
        new(6, Team.Dire, Lane.Top, lastHits: 40, 3, 3500, 2800, 0),
    ];

    private static readonly Dictionary<int, int> Roles = new() { [1] = 1, [2] = 5, [6] = 1 };

    // Points at 0:00, 10:00, 20:00 and the end (25:00).
    private static readonly MatchTimeline Timeline = new(
        [0, 600, 1200, 1500],
        [
            new HeroTimeline(1, Team.Radiant, [600, 4000, 9000, 12000], [0, 1, 2, 3]),
            new HeroTimeline(2, Team.Radiant, [600, 2000, 4000, 4000], [0, 1, 2, 3]),
            new HeroTimeline(6, Team.Dire, [600, 3500, 8000, 10000], [0, 1, 2, 3]),
        ]);

    [Fact]
    public void Deaths_IsolatedAndOnTheEnemySide()
    {
        var positions = new[]
        {
            // The support stands next to the carry's first death and far from the second.
            Path(2, (100, -3000, -3000), (500, 5000, 4000)),
            Path(1),
        };
        var deaths = new[]
        {
            new HeroDeath(1, 101, -3200, -3000, KillerKind.Hero, 6, reincarnated: false), // ally 200 away
            new HeroDeath(1, 499, 6000, 6000, KillerKind.Hero, 6, reincarnated: false),   // ally ~2200 away, enemy side
        };

        var carry = Metrics(positions, deaths: deaths).Single(hero => hero.HeroId == 1);

        Assert.Equal((2, 1, 1), (carry.Deaths, carry.IsolatedDeaths, carry.DeathsOnEnemySide));
    }

    [Fact]
    public void TimeShares_CountTheSamplesAliveFromTheHorn()
    {
        var positions = new[]
        {
            // 4 samples from 0:00 (one before the horn is left out): 2 on the enemy side, 1 of them with the support near.
            Path(1, (-30, 5000, 5000), (0, -4000, -4000), (2, 5000, 5000), (4, 5000, 5000), (6, 0, 0)),
            Path(2, (2, 5200, 5100)),
        };

        var carry = Metrics(positions).Single(hero => hero.HeroId == 1);

        Assert.Equal(0.5, carry.EnemySideShare);
        Assert.Equal(0.25, carry.AloneOnEnemySideShare);
    }

    [Fact]
    public void Economy_FromTheTimelineAndTheLaningPhase()
    {
        var metrics = Metrics([]);
        var carry = metrics.Single(hero => hero.HeroId == 1);

        Assert.Equal((4000, 9000, 50), (carry.NetWorthAt10, carry.NetWorthAt20, carry.LastHitsAt10));
        Assert.Equal(0.75, carry.NetWorthShare); // 12000 of 16000
        Assert.Equal(1.0, metrics.Single(hero => hero.HeroId == 6).NetWorthShare);
    }

    [Fact]
    public void Vision_PlacedLifetimeLostAndDestroyed()
    {
        var wards = new[]
        {
            new WardPlacement(WardKind.Observer, Team.Radiant, 2, 0, 0, 100, 460, destroyed: false, null), // 360 s
            new WardPlacement(WardKind.Observer, Team.Radiant, 2, 0, 0, 1400, null, destroyed: false, null), // to the end: 100 s
            new WardPlacement(WardKind.Sentry, Team.Radiant, 2, 0, 0, 200, 250, destroyed: true, 6),
            new WardPlacement(WardKind.Observer, Team.Dire, 6, 0, 0, 300, 320, destroyed: true, 2),
        };

        var support = Metrics([], wards: wards).Single(hero => hero.HeroId == 2);

        Assert.Equal((2, 1, 230, 1, 1), (support.ObserversPlaced, support.SentriesPlaced,
            support.ObserverAverageLifetimeSeconds, support.WardsLostToEnemy, support.EnemyWardsDestroyed));
    }

    [Fact]
    public void Regions_TimeAndDeaths_FromTheHerosOwnSide()
    {
        // The Dire carry: 1 sample in its base, 1 in the middle lane, 2 in Radiant's jungle (its enemy jungle).
        var positions = new[] { Path(6, (0, 7000, 7000), (2, 0, 0), (4, -3500, 0), (6, -3500, 100)) };
        var deaths = new[] { new HeroDeath(6, 7, -3500, 100, KillerKind.Hero, 1, reincarnated: false) };

        var dire = Metrics(positions, deaths: deaths).Single(hero => hero.HeroId == 6);

        Assert.Equal(new PerRegion<double>(0.25, 0, 0, 0.25, 0, 0, 0, 0.5), dire.TimeByRegion);
        Assert.Equal(new PerRegion<int>(0, 0, 0, 0, 0, 0, 0, 1), dire.DeathsByRegion);
    }

    [Fact]
    public void Regions_TimeAndDeaths_AreSplitAtTheTenAndTwentyMinuteBoundaries()
    {
        var positions = new[]
        {
            Path(1,
                (0, -7000, -7000), (598, -7000, -7000),
                (600, 7000, 7000), (1198, -7000, -7000),
                (1200, 7000, 7000), (1202, 7000, 7000)),
        };
        var deaths = new[]
        {
            new HeroDeath(1, 599, -7000, -7000, KillerKind.Hero, 6, reincarnated: false),
            new HeroDeath(1, 600, 7000, 7000, KillerKind.Hero, 6, reincarnated: false),
            new HeroDeath(1, 1199, -7000, -7000, KillerKind.Hero, 6, reincarnated: false),
            new HeroDeath(1, 1200, 7000, 7000, KillerKind.Hero, 6, reincarnated: false),
        };

        var carry = Metrics(positions, deaths: deaths).Single(hero => hero.HeroId == 1);

        Assert.Equal(new PerGamePhase<PerRegion<double>>(
            new PerRegion<double>(1, 0, 0, 0, 0, 0, 0, 0),
            new PerRegion<double>(0.5, 0.5, 0, 0, 0, 0, 0, 0),
            new PerRegion<double>(0, 1, 0, 0, 0, 0, 0, 0)), carry.TimeByRegionByPhase);
        Assert.Equal(new PerGamePhase<PerRegion<int>>(
            new PerRegion<int>(1, 0, 0, 0, 0, 0, 0, 0),
            new PerRegion<int>(1, 1, 0, 0, 0, 0, 0, 0),
            new PerRegion<int>(0, 1, 0, 0, 0, 0, 0, 0)), carry.DeathsByRegionByPhase);
    }

    [Fact]
    public void Movement_EstimatesAdjacentDisplacementByMidpointRegionAndSkipsGaps()
    {
        var positions = new[]
        {
            Path(1,
                (0, -6450, -3000),
                (2, -6450, -2900),
                (4, -6450, -2700),
                (10, -6450, -2600)),
        };

        var carry = Metrics(positions).Single(hero => hero.HeroId == 1);

        Assert.Equal(300, carry.EstimatedMovementDistance);
        Assert.Equal(2, carry.MovementSampleSegments);
        Assert.Equal(new PerRegion<double>(0, 0, 300, 0, 0, 0, 0, 0), carry.EstimatedMovementByRegion);
    }

    [Fact]
    public void Fights_PresenceKillParticipationAndQuickRedeaths()
    {
        // A fight at 10:00 around (0, 0): the Dire carry dies (killed by the Radiant carry, the support assists),
        // then both Radiant heroes die. The support was there; the carry was far away until it died.
        var deaths = new[]
        {
            new HeroDeath(6, 600, 0, 0, KillerKind.Hero, 1, reincarnated: false, assistHeroIds: [2]),
            new HeroDeath(2, 605, 100, 0, KillerKind.Hero, 6, reincarnated: false),
            new HeroDeath(1, 610, -100, 0, KillerKind.Hero, 6, reincarnated: false),
            // Back at 650 (first position after the death), dead again at 700: a quick re-death.
            new HeroDeath(1, 700, -5000, -5000, KillerKind.Tower, null, reincarnated: false),
        };
        var positions = new[]
        {
            Path(1, (596, 6000, 6000), (650, -6000, -6000)),
            Path(2, (598, 50, 50)),
        };

        var metrics = Metrics(positions, deaths: deaths);
        var carry = metrics.Single(hero => hero.HeroId == 1);
        var support = metrics.Single(hero => hero.HeroId == 2);

        Assert.Equal((1, 0, 1.0, 1), (carry.TeamFights, carry.FightsNearby, carry.KillParticipation, carry.QuickRedeaths));
        Assert.Equal((1, 1, 1.0, 0), (support.TeamFights, support.FightsNearby, support.KillParticipation, support.QuickRedeaths));
        // The Dire carry killed two of the three Radiant deaths (the tower took the last): 2 of 3.
        Assert.Equal(2.0 / 3, metrics.Single(hero => hero.HeroId == 6).KillParticipation!.Value, precision: 6);
    }

    [Fact]
    public void FarmByRegion_EachMinuteGoesToWhereTheHeroSpentMostOfIt()
    {
        // In a real timeline point i is minute i; this fixture's points 0, 1, 2 stand for minutes 0, 1, 2 (the last
        // point, the end, is never a full minute). Carry net worth: 600 -> 4000 -> 9000. Minute 0 (0-59 s): mostly in
        // the top lane; minute 1 (60-119 s): in Dire's jungle (the enemy jungle for Radiant).
        var positions = new[] { Path(1, (0, -6450, 0), (2, -6450, 0), (4, 3500, 0), (60, 3500, 0), (62, 3500, 0)) };

        var carry = Metrics(positions).Single(hero => hero.HeroId == 1);

        Assert.Equal(3400, carry.NetWorthGainByRegion!.TopLane);
        Assert.Equal(5000, carry.NetWorthGainByRegion.EnemyJungle);
        Assert.Equal((1, 1), (carry.MinutesByRegion!.TopLane, carry.MinutesByRegion.EnemyJungle));
    }

    [Fact]
    public void ObserverUptime_IsTheUnionOfTheTeamsObservers()
    {
        // The match lasts 1500 s. Radiant observers: 0-360 and 300-660 overlap (660 s), plus 1400-end (100 s); a sentry
        // doesn't count. Dire has none.
        var wards = new[]
        {
            new WardPlacement(WardKind.Observer, Team.Radiant, 2, 0, 0, 0, 360, destroyed: false, null),
            new WardPlacement(WardKind.Observer, Team.Radiant, 1, 0, 0, 300, 660, destroyed: false, null),
            new WardPlacement(WardKind.Observer, Team.Radiant, 2, 0, 0, 1400, null, destroyed: false, null),
            new WardPlacement(WardKind.Sentry, Team.Radiant, 2, 0, 0, 800, 1200, destroyed: false, null),
        };

        var metrics = Metrics([], wards: wards);

        Assert.Equal(760.0 / 1500, metrics.Single(hero => hero.HeroId == 1).TeamObserverUptime!.Value, precision: 6);
        Assert.Equal(0, metrics.Single(hero => hero.HeroId == 6).TeamObserverUptime);
    }

    [Fact]
    public void LeadLostPerDeath_FromTheMinuteOfTheDeathToTheNext()
    {
        // Points at 0:00, 10:00, 20:00 and the end, so only a death in minute 0 has a "next minute" (10:00 here is
        // point 1). Radiant lead at point 0: 600 + 600 - 600 = 600; at point 1: 4000 + 2000 - 3500 = 2500.
        var deaths = new[]
        {
            new HeroDeath(1, 30, 0, 0, KillerKind.Hero, 6, reincarnated: false),
            new HeroDeath(1, 40, 0, 0, KillerKind.Hero, 6, reincarnated: true), // Aegis: left out
        };

        var carry = Metrics([], deaths: deaths).Single(hero => hero.HeroId == 1);

        Assert.Equal(600 - 2500, carry.LeadLostPerDeath);
        Assert.Null(Metrics([]).Single(hero => hero.HeroId == 1).LeadLostPerDeath);
    }

    [Fact]
    public void Movement_LeavesOutTeleportsAndBlinks()
    {
        // 100 units walked, then a 5000-unit jump in 2 s (a TP): only the walk counts.
        var carry = Metrics([Path(1, (0, -6450, -3000), (2, -6450, -2900), (4, -1450, -2900))])
            .Single(hero => hero.HeroId == 1);

        Assert.Equal(100, carry.EstimatedMovementDistance);
        Assert.Equal(1, carry.MovementSampleSegments);
    }

    [Fact]
    public void Movement_IsNullWhenThereAreNoContinuousPositionSamples()
    {
        var carry = Metrics([Path(1, (0, -6450, -3000), (5, -6450, -2900))])
            .Single(hero => hero.HeroId == 1);

        Assert.Null(carry.EstimatedMovementDistance);
        Assert.Null(carry.EstimatedMovementByRegion);
        Assert.Equal(0, carry.MovementSampleSegments);
    }

    [Fact]
    public void WithoutRoleQueue_ThereAreNoMetrics()
    {
        Assert.Null(MatchMetricsCalculator.Calculate(Players, Timeline, [], [], [], roles: null));
        Assert.Null(MatchMetricsCalculator.Calculate(Players, Timeline, [], [], [], new Dictionary<int, int> { [1] = 1 }));
    }

    [Theory]
    [InlineData(Team.Radiant, 800, 800, true)]   // ~1131 past the river
    [InlineData(Team.Radiant, 600, 600, false)]  // ~849: still the river
    [InlineData(Team.Dire, -800, -800, true)]
    [InlineData(Team.Dire, 800, 800, false)]
    public void EnemySide_StartsPastTheRiver(Team team, int x, int y, bool expected)
    {
        Assert.Equal(expected, MatchMetricsCalculator.IsEnemySide(team, x, y));
    }

    private static IReadOnlyList<HeroMetrics> Metrics(
        IReadOnlyList<HeroPositions> positions, IReadOnlyList<HeroDeath>? deaths = null, IReadOnlyList<WardPlacement>? wards = null) =>
        MatchMetricsCalculator.Calculate(Players, Timeline, deaths ?? [], wards ?? [], positions, Roles)!;

    private static HeroPositions Path(int heroId, params (int Seconds, int X, int Y)[] samples) =>
        new(heroId, samples.Select(sample => new PositionSample(sample.Seconds, sample.X, sample.Y)).ToList());
}
