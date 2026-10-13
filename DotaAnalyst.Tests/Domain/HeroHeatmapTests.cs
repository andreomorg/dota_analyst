using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class HeroHeatmapTests
{
    [Theory]
    [InlineData(-9472, 9472, 0, 0)] // north-west corner of the minimap
    [InlineData(9471, -9471, 31, 31)] // south-east corner
    [InlineData(0, 0, 16, 16)] // the center of the river
    [InlineData(-20000, 20000, 0, 0)] // off the map: clamped
    [InlineData(20000, -20000, 31, 31)]
    public void CellOf_LinesUpWithTheMinimap(int x, int y, int column, int row) =>
        Assert.Equal((column, row), HeroHeatmap.CellOf(x, y));

    [Fact]
    public void FromPositions_CountsSamplesPerCell_ByPhase_FromZero()
    {
        var positions = new HeroPositions(31,
        [
            new PositionSample(-30, 0, 0), // before 0:00: left out
            new PositionSample(10, 0, 0),
            new PositionSample(12, 100, -100), // the same cell
            new PositionSample(598, -9000, 9000),
            new PositionSample(600, 0, 0), // 10:00: mid game
            new PositionSample(1200, 9000, -9000), // 20:00: late game
        ]);

        var heatmap = HeroHeatmap.FromPositions(positions, []);

        Assert.Equal(31, heatmap.HeroId);
        Assert.Equal([new HeatCell(0, 0, 1), new HeatCell(16, 16, 2)], heatmap.Phases.EarlyGame);
        Assert.Equal([new HeatCell(16, 16, 1)], heatmap.Phases.MidGame);
        Assert.Equal([new HeatCell(31, 31, 1)], heatmap.Phases.LateGame);
        Assert.Equal(new TeamProximity(0, 0), heatmap.Proximity!.EarlyGame);
    }

    [Fact]
    public void FromPositions_CountsTheSamplesWithAlliesCloseBy()
    {
        var me = new HeroPositions(31, [new PositionSample(10, 0, 0), new PositionSample(12, 0, 0), new PositionSample(14, 0, 0)]);
        // At 10 s two allies are close; at 12 s one (the other is 2000 away); at 14 s none is alive.
        var first = new HeroPositions(5, [new PositionSample(10, 1000, 0), new PositionSample(12, 0, 1400)]);
        var second = new HeroPositions(2, [new PositionSample(10, -500, -500), new PositionSample(12, 2000, 0)]);

        var heatmap = HeroHeatmap.FromPositions(me, [first, second]);

        Assert.Equal(new TeamProximity(NearAlly: 2, Grouped: 1), heatmap.Proximity!.EarlyGame);
    }

    [Fact]
    public void RefusesProximityThatCountsMoreThanThePhasesSamples() =>
        Assert.Throws<ArgumentException>(() => new HeroHeatmap(31,
            new PerGamePhase<IReadOnlyList<HeatCell>>([new HeatCell(1, 1, 2)], [], []),
            new PerGamePhase<TeamProximity>(new TeamProximity(3, 0), default, default)));

    [Theory]
    [InlineData(-1, 0, 1)]
    [InlineData(0, 32, 1)]
    [InlineData(0, 0, 0)]
    public void RefusesCellsOffTheGridOrWithoutSamples(int column, int row, int samples) =>
        Assert.ThrowsAny<ArgumentException>(() => new HeroHeatmap(31,
            new PerGamePhase<IReadOnlyList<HeatCell>>([new HeatCell(column, row, samples)], [], [])));

    [Fact]
    public void RefusesTheSameCellTwice() =>
        Assert.Throws<ArgumentException>(() => new HeroHeatmap(31,
            new PerGamePhase<IReadOnlyList<HeatCell>>([new HeatCell(1, 1, 1), new HeatCell(1, 1, 2)], [], [])));
}
