using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class HeatmapIndicatorsTests
{
    private static HeroHeatmap Heatmap(IReadOnlyList<HeatCell> early, IReadOnlyList<HeatCell>? mid = null, PerGamePhase<TeamProximity>? proximity = null) =>
        new(31, new PerGamePhase<IReadOnlyList<HeatCell>>(early, mid ?? [], []), proximity);

    [Fact]
    public void Advance_FromTheOwnFountainToTheEnemys_ForEachTeam()
    {
        // Cell (3, 28) is near the Radiant fountain (bottom-left), (28, 3) near the Dire's, (16, 16) the river.
        var nearRadiant = Heatmap([new HeatCell(3, 28, 10)]);
        var river = Heatmap([new HeatCell(16, 16, 10)]);

        Assert.InRange(HeatmapIndicators.Calculate(nearRadiant, Team.Radiant)!.Advance, 0, 0.1);
        Assert.InRange(HeatmapIndicators.Calculate(nearRadiant, Team.Dire)!.Advance, 0.9, 1);
        Assert.InRange(HeatmapIndicators.Calculate(river, Team.Radiant)!.Advance, 0.45, 0.55);
    }

    [Fact]
    public void Coverage_Concentration_EnemySide_AndTime()
    {
        // 10 samples in the Radiant fountain cell, 1 sample in each of 6 cells on Dire's side (beyond the river).
        IReadOnlyList<HeatCell> cells =
        [
            new HeatCell(3, 28, 10),
            .. Enumerable.Range(20, 6).Select(column => new HeatCell(column, 8, 1)),
        ];

        var indicators = HeatmapIndicators.Calculate(Heatmap(cells), Team.Radiant)!;

        Assert.Equal(32, indicators.AliveSeconds);
        // Only the fountain cell has the 2 samples that make a cell covered.
        Assert.Equal(1.0 / HeatmapIndicators.PlayableCells, indicators.Coverage, 6);
        // The 5 most visited cells: the fountain (10) and four of the others (1 each).
        Assert.Equal(14.0 / 16, indicators.Concentration, 6);
        Assert.Equal(6.0 / 16, indicators.EnemySide, 6);
        Assert.Null(indicators.NearAlly);
    }

    [Fact]
    public void Phases_AddUp_AndProximityIsAShareOfTheirSamples()
    {
        var heatmap = Heatmap(
            [new HeatCell(16, 16, 4)],
            [new HeatCell(16, 16, 6)],
            new PerGamePhase<TeamProximity>(new TeamProximity(4, 2), new TeamProximity(1, 0), default));

        var whole = HeatmapIndicators.Calculate(heatmap, Team.Radiant)!;
        var early = HeatmapIndicators.Calculate(heatmap, Team.Radiant, GamePhase.EarlyGame)!;

        Assert.Equal((0.5, 0.2), (whole.NearAlly!.Value, whole.Grouped!.Value));
        Assert.Equal((1.0, 0.5), (early.NearAlly!.Value, early.Grouped!.Value));
        Assert.Null(HeatmapIndicators.Calculate(heatmap, Team.Radiant, GamePhase.LateGame));
    }
}
