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
