using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

// VisionMetrics: what the fog of war showed around a hero, from the visibility of each position sample.
public class VisionMetricsTests
{
    private const int Hero = 1;
    private const int Enemy = 2;

    // Samples every 2 s from `from` to `to` (inclusive), all seen or unseen by the other team.
    private static IEnumerable<PositionSample> Samples(int from, int to, bool? seen) =>
        Enumerable.Range(0, (to - from) / 2 + 1)
            .Select(i => new PositionSample(from + 2 * i, 0, 0, new HeroVitals(500, 500, 100, 100, VisibleToEnemies: seen)));

    private static Dictionary<int, HeroPositions> Positions(IEnumerable<PositionSample> hero, IEnumerable<PositionSample> enemy) =>
        new() { [Hero] = new HeroPositions(Hero, hero.ToList()), [Enemy] = new HeroPositions(Enemy, enemy.ToList()) };

    [Fact]
    public void SeenShare_CountsOnlySamplesFromTheHorn()
    {
        var positions = Positions(Samples(-20, -2, true).Concat(Samples(0, 6, true)).Concat(Samples(8, 14, false)), Samples(0, 14, false));

        var vision = VisionMetricsCalculator.Calculate(Hero, positions, [])!;

        Assert.Equal(0.5, vision.SeenByEnemiesShare, 6);
    }

    [Fact]
    public void DeathAfterBeingWatched_IsTracked_AndTheKillerSeenIsNotFromTheFog()
    {
        // Seen for the whole 10 s before dying at 100; the killer was in sight of the hero's team.
        var positions = Positions(Samples(0, 98, true), Samples(0, 120, true));
        IReadOnlyList<HeroDeath> deaths = [new(Hero, 100, 0, 0, KillerKind.Hero, Enemy, false)];

        var vision = VisionMetricsCalculator.Calculate(Hero, positions, deaths)!;

        Assert.Equal(1, vision.DeathsWhileTracked);
        Assert.Equal(0, vision.DeathsUnseenBefore);
        Assert.Equal(0, vision.DeathsToKillerFromFog);
    }

    [Fact]
    public void GankFromTheFog_CountsForTheVictim_AndAsAKillFromTheFogForTheKiller()
    {
        // Unseen before dying at 100; the killer unseen by the hero's team in the 6 s before (seen at the kill itself).
        var positions = Positions(Samples(0, 98, false),
            Samples(0, 92, true).Concat(Samples(94, 98, false)).Concat(Samples(100, 120, true)));
        IReadOnlyList<HeroDeath> deaths = [new(Hero, 100, 0, 0, KillerKind.Hero, Enemy, false)];

        var victim = VisionMetricsCalculator.Calculate(Hero, positions, deaths)!;
        var killer = VisionMetricsCalculator.Calculate(Enemy, positions, deaths)!;

        Assert.Equal(1, victim.DeathsUnseenBefore);
        Assert.Equal(1, victim.DeathsToKillerFromFog);
        Assert.Equal(1, killer.KillsFromFog);
    }

    [Fact]
    public void WithoutVisibility_NoMetrics_AndUnknownKillerWindowIsNull()
    {
        Assert.Null(VisionMetricsCalculator.Calculate(Hero, Positions(Samples(0, 20, null), Samples(0, 20, null)), []));

        // The killer has no samples before the kill: its origin is unknown.
        var positions = Positions(Samples(0, 98, true), Samples(100, 120, true));
        IReadOnlyList<HeroDeath> deaths = [new(Hero, 100, 0, 0, KillerKind.Hero, Enemy, false)];
        Assert.Null(VisionMetricsCalculator.Calculate(Hero, positions, deaths)!.DeathsToKillerFromFog);
        Assert.Null(VisionMetricsCalculator.Calculate(Enemy, positions, deaths)!.KillsFromFog);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InterleavedUnknownVisibility_InvalidatesDeathAndKillCounters(bool seen)
    {
        var samples = Samples(90, 98, seen).Select(sample => sample.Seconds is 92 or 96
            ? sample with { Vitals = sample.Vitals! with { VisibleToEnemies = null } } : sample);
        var positions = Positions(samples, Samples(90, 100, true));
        HeroDeath[] deaths = [new(Hero, 100, 0, 0, KillerKind.Hero, Enemy, false),
            new(Enemy, 100, 0, 0, KillerKind.Hero, Hero, false)];

        var vision = VisionMetricsCalculator.Calculate(Hero, positions, deaths)!;

        Assert.Null(vision.DeathsWhileTracked);
        Assert.Null(vision.DeathsUnseenBefore);
        Assert.Null(vision.KillsFromFog);
    }

    [Theory]
    [InlineData(90, 94)]
    [InlineData(98, -1)]
    public void MissingSampleOrRecentRespawn_InvalidatesWindow(int from, int missing)
    {
        var positions = Positions(Samples(from, 98, false).Where(sample => sample.Seconds != missing), Samples(90, 100, true));
        HeroDeath[] deaths = [new(Hero, 100, 0, 0, KillerKind.Hero, Enemy, false)];

        var vision = VisionMetricsCalculator.Calculate(Hero, positions, deaths)!;

        Assert.Null(vision.DeathsWhileTracked);
        Assert.Null(vision.DeathsUnseenBefore);
    }

    [Fact]
    public void PreviousDeathWithinWindow_InvalidatesEvenAnOtherwiseCompleteTrack()
    {
        var positions = Positions(Samples(80, 100, false), Samples(80, 100, false));
        HeroDeath[] deaths = [new(Hero, 96, 0, 0, KillerKind.Hero, Enemy, true),
            new(Hero, 100, 0, 0, KillerKind.Hero, Enemy, false),
            new(Enemy, 100, 0, 0, KillerKind.Hero, Hero, false)];

        var vision = VisionMetricsCalculator.Calculate(Hero, positions, deaths)!;

        Assert.Null(vision.DeathsWhileTracked);
        Assert.Null(vision.DeathsUnseenBefore);
        Assert.Null(vision.KillsFromFog);
    }

    [Fact]
    public void UnknownEventInvalidatesTotalEvenWhenAnotherEventIsKnown()
    {
        var positions = Positions(Samples(80, 98, true), Samples(94, 98, false));
        HeroDeath[] deaths = [new(Hero, 100, 0, 0, KillerKind.Hero, Enemy, false),
            new(Hero, 200, 0, 0, KillerKind.Hero, Enemy, false)];

        var vision = VisionMetricsCalculator.Calculate(Hero, positions, deaths)!;

        Assert.Null(vision.DeathsWhileTracked);
        Assert.Null(vision.DeathsUnseenBefore);
        Assert.Null(vision.DeathsToKillerFromFog);
        Assert.Equal(0, vision.KillsFromFog);
    }

    [Theory]
    [InlineData(94, 96)]
    [InlineData(98, -1)]
    public void KillerWithGapOrRecentRespawn_HasUnknownFogCounters(int from, int missing)
    {
        var positions = Positions(Samples(90, 98, true),
            Samples(from, 98, false).Where(sample => sample.Seconds != missing));
        HeroDeath[] deaths = [new(Hero, 100, 0, 0, KillerKind.Hero, Enemy, false)];

        Assert.Null(VisionMetricsCalculator.Calculate(Hero, positions, deaths)!.DeathsToKillerFromFog);
        Assert.Null(VisionMetricsCalculator.Calculate(Enemy, positions, deaths)!.KillsFromFog);
    }

    [Fact]
    public void NonHeroKillerIsEvaluable_WhileUnattributedHeroKillerIsUnknown()
    {
        var positions = Positions(Samples(90, 98, true), []);
        HeroDeath[] deaths = [new(Hero, 100, 0, 0, KillerKind.Tower, null, false)];
        Assert.Equal(0, VisionMetricsCalculator.Calculate(Hero, positions, deaths)!.DeathsToKillerFromFog);

        deaths = [new(Hero, 100, 0, 0, KillerKind.Hero, null, false)];
        Assert.Null(VisionMetricsCalculator.Calculate(Hero, positions, deaths)!.DeathsToKillerFromFog);
    }

    [Fact]
    public void NoEvents_HasMeasuredZeroCounters()
    {
        var vision = VisionMetricsCalculator.Calculate(Hero, Positions(Samples(0, 2, true), []), [])!;
        Assert.Equal(0, vision.DeathsWhileTracked);
        Assert.Equal(0, vision.DeathsUnseenBefore);
        Assert.Equal(0, vision.DeathsToKillerFromFog);
        Assert.Equal(0, vision.KillsFromFog);
    }

    [Theory]
    [InlineData(1536, 384, true)]
    [InlineData(1536, 512, false)]
    public void ObserverRadius_UsesMapUnitsAtTheOuterEdge(int x, int y, bool inside)
    {
        // Only this outer point is walkable; an excluded point leaves the denominator empty.
        var grid = new MapGrid(64, 1, 1, x, y, [1], [0]);
        var map = new GameMap("test", grid, [], [], [], [], [], [], [], []);

        var share = VisionMetricsCalculator.SeenShare(map, 0, 0, new HashSet<int>());

        if (inside)
        {
            Assert.Equal(1.0, share);
        }
        else
        {
            Assert.Null(share);
        }
    }
}
