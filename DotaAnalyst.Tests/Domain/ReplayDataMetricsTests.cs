using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

// The statistics that read replay data only the prediction models used before: fight initiation (hero hits), lane
// pressure (waves), day/night, smokes and farm (creep kills).
public class ReplayDataMetricsTests
{
    private const int Hero = 1;
    private static readonly Dictionary<int, Team> Teams = new() { [1] = Team.Radiant, [2] = Team.Radiant, [6] = Team.Dire, [7] = Team.Dire };

    private static Teamfight Fight(int start, int end) => new(start, end, 1, 2, Team.Radiant, []);

    [Fact]
    public void FightInitiation_TheFirstHitsDecide_AndAClashCountsForNeither()
    {
        var hits = new List<HeroHit>
        {
            // Fight 1 (500-520): the hero hits first at 488 (within the 15 s lead-in).
            new(488, 1, 6, 100), new(490, 6, 1, 80),
            // Fight 2 (900-920): the enemy hits first.
            new(896, 7, 2, 50), new(898, 1, 7, 60),
            // Fight 3 (1300-1310): both teams in the same first bucket.
            new(1296, 2, 6, 40), new(1296, 6, 2, 40),
            // An ally hitting an ally (illusion swap, deny) never counts.
            new(1290, 2, 1, 10),
        };

        var metrics = FightInitiationMetricsCalculator.Calculate(Hero, Team.Radiant,
            [Fight(500, 520), Fight(900, 920), Fight(1300, 1310)], hits, Teams);

        Assert.Equal((3, 1, 1), (metrics.Fights, metrics.TeamStarted!.Value, metrics.HeroStarted!.Value));
        Assert.Null(FightInitiationMetricsCalculator.Calculate(Hero, Team.Radiant, [Fight(500, 520)], null, Teams).TeamStarted);
    }

    [Fact]
    public void LanePressure_IsSeenFromTheHerosSide()
    {
        // Radiant's front at 600 and Dire's at 1000: they meet at 800, past the river for Radiant.
        var waves = new List<WaveFront>
        {
            new(Lane.Middle, 60, 600, 1000), new(Lane.Middle, 62, -1000, -200),
            new(Lane.Middle, 700, 3000, 3000), // after 10:00: not the laning phase
            new(Lane.Top, 60, 0, 0),
        };

        var radiant = LanePressureMetricsCalculator.Calculate(Team.Radiant, Lane.Middle, waves)!;
        var dire = LanePressureMetricsCalculator.Calculate(Team.Dire, Lane.Middle, waves)!;

        Assert.Equal((2, 0.5, 100), (radiant.Samples, radiant.PushedShare, radiant.AverageMeetingPoint));
        Assert.Equal((0.5, -100), (dire.PushedShare, dire.AverageMeetingPoint));
        Assert.Null(LanePressureMetricsCalculator.Calculate(Team.Radiant, Lane.Roaming, waves));
        Assert.Null(LanePressureMetricsCalculator.Calculate(Team.Radiant, Lane.Middle, null));
    }

    [Fact]
    public void DayNight_SplitsKillsAndDeathsByTheCycle()
    {
        // Day until 300, night 300-600, day after.
        var cycle = new List<DayNightChange> { new(0, true), new(300, false), new(600, true) };
        var deaths = new List<HeroDeath>
        {
            new(6, 100, 0, 0, KillerKind.Hero, 1, false),
            new(7, 400, 0, 0, KillerKind.Hero, 2, false, [1]),
            new(1, 500, 0, 0, KillerKind.Hero, 6, false),
        };

        var metrics = DayNightMetricsCalculator.Calculate(Hero, Team.Radiant, deaths, Teams, cycle, 1200)!;

        Assert.Equal((0.25, 2, 1, 1, 1), (metrics.NightShare, metrics.Kills, metrics.KillsAtNight, metrics.Deaths, metrics.DeathsAtNight));
        Assert.Null(DayNightMetricsCalculator.Calculate(Hero, Team.Radiant, deaths, Teams, null, 1200));
    }

    [Fact]
    public void Smokes_CountTheOnesFollowedByAnEnemyDeath()
    {
        var activity = new ReplayActivity([new(Hero, Team.Radiant, 100), new(Hero, Team.Radiant, 500), new(2, Team.Radiant, 900)], [], []);
        var deaths = new List<HeroDeath> { new(6, 150, 0, 0, KillerKind.Hero, 2, false), new(1, 520, 0, 0, KillerKind.Hero, 6, false) };

        var metrics = SmokeMetricsCalculator.Calculate(Hero, Team.Radiant, activity, deaths, Teams)!;

        // The ally's smoke is not the hero's; the hero's own death after the second smoke is not a kill.
        Assert.Equal((2, 1), (metrics.Smokes, metrics.SmokesWithKill));
    }

    [Fact]
    public void Farm_ByKind_AndAfterTheLaningPhase()
    {
        var kills = new List<CreepKill>
        {
            new(Hero, 100, CreepKillKind.Lane, 0, 0), new(Hero, 200, CreepKillKind.Deny, 0, 0),
            new(Hero, 700, CreepKillKind.Lane, 0, 0), new(Hero, 800, CreepKillKind.Neutral, 0, 0),
            new(2, 900, CreepKillKind.Neutral, 0, 0),
        };

        var metrics = FarmMetricsCalculator.Calculate(Hero, kills)!;

        Assert.Equal(new FarmMetrics(2, 1, 1, 1, 1), metrics);
        Assert.Null(FarmMetricsCalculator.Calculate(Hero, null));
    }
}
