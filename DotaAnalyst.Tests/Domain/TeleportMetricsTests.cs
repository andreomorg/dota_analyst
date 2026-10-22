using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

// TeleportMetrics: teleports done, cancelled and into fights, from the TP channel and the jump of the hero's position.
public class TeleportMetricsTests
{
    private const int Hero = 1;

    private static Buff Tp(double start, double? end) => new(start, end, Hero, Hero, "modifier_teleporting", BuffKind.Teleport);

    private static HeroPositions Path(params (int Seconds, int X, int Y)[] samples) =>
        new(Hero, samples.Select(sample => new PositionSample(sample.Seconds, sample.X, sample.Y)).ToList());

    private static Teamfight FightAt(int start, int end, int x, int y) =>
        new(start, end, 2, 1, Team.Radiant,
            [new HeroDeath(6, start, x, y, KillerKind.Hero, 2, false), new HeroDeath(7, end, x, y, KillerKind.Hero, 2, false),
             new HeroDeath(3, end, x, y, KillerKind.Hero, 8, false)]);

    [Fact]
    public void CompletedIntoAFight_CancelledInPlace_AndDiedChannelling()
    {
        // TP 1: base -> mid at 103, a fight there from 110. TP 2: stays put. TP 3: dies while channelling.
        var path = Path((100, -6000, -6000), (102, -6000, -6000), (104, 0, 0), (200, 0, 0), (202, 0, 0), (206, 0, 0), (300, 0, 0));
        IReadOnlyList<Buff> buffs = [Tp(100, 103), Tp(200, 202), Tp(300, 302)];
        IReadOnlyList<HeroDeath> deaths = [new(Hero, 302, 0, 0, KillerKind.Hero, 6, false)];

        var metrics = TeleportMetricsCalculator.Calculate(Hero, buffs, path, [FightAt(110, 125, 500, 300)], deaths)!;

        Assert.Equal(1, metrics.Teleports);
        Assert.Equal(2, metrics.Cancelled);
        Assert.Equal(1, metrics.IntoFights);
    }

    [Fact]
    public void UnknownOutcome_MakesEveryCountUnknown_AndNoBuffsNoMetrics()
    {
        // The channel never ends in the document.
        var metrics = TeleportMetricsCalculator.Calculate(Hero, [Tp(100, null)], Path((98, 0, 0)), [], [])!;

        Assert.Null(metrics.Teleports);
        Assert.Null(metrics.IntoFights);
        Assert.Null(TeleportMetricsCalculator.Calculate(Hero, null, Path((98, 0, 0)), [], []));
    }
}
