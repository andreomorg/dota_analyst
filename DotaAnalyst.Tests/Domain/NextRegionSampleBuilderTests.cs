using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Situations;

namespace DotaAnalyst.Tests.Domain;

public class NextRegionSampleBuilderTests
{
    private static readonly LaningStats[] Players =
    [
        new(1, Team.Radiant, Lane.Bottom, 0, 0, 600, 0, 0),
        new(6, Team.Dire, Lane.Top, 0, 0, 600, 0, 0),
    ];

    // The match ends at 1:30.
    private static readonly MatchTimeline Timeline = new(
        [0, 60, 90],
        [new HeroTimeline(1, Team.Radiant, [600, 700, 750], [0, 100, 150]), new HeroTimeline(6, Team.Dire, [600, 700, 750], [0, 100, 150])]);

    [Fact]
    public void Label_IsTheRegionThirtySecondsLater_UntilTheLastFullHorizon()
    {
        // In the Radiant jungle until 0:40, then in the Dire jungle.
        var positions = new HeroPositions(1, Enumerable.Range(0, 46)
            .Select(i => i * 2)
            .Select(second => second <= 40 ? new PositionSample(second, -3000, -1000) : new PositionSample(second, 3000, 1000))
            .ToList());

        var samples = Build(positions);

        // 0 ... 60 (60 + 30 = 1:30, the end).
        Assert.Equal([0, 10, 20, 30, 40, 50, 60], samples.Select(sample => sample.Situation.Seconds));
        Assert.Equal(MapRegions.RegionOf(Team.Radiant, -3000, -1000), samples.Single(sample => sample.Situation.Seconds == 10).RegionLater);
        Assert.Equal(MapRegions.RegionOf(Team.Radiant, 3000, 1000), samples.Single(sample => sample.Situation.Seconds == 20).RegionLater);
    }

    [Fact]
    public void DeadThirtySecondsLater_HasNoRegion()
    {
        // Dead from 0:42 to 0:58 (no samples).
        var positions = new HeroPositions(1, Enumerable.Range(0, 46).Select(i => i * 2)
            .Where(second => second is < 42 or > 58)
            .Select(second => new PositionSample(second, 0, 0)).ToList());

        var at20 = Build(positions).Single(sample => sample.Situation.Seconds == 20);

        Assert.Null(at20.RegionLater);
    }

    [Fact]
    public void OutdatedAnalysis_HasNoSamples()
    {
        Assert.Null(NextRegionSampleBuilder.Build(new ReplayAnalysis(Match(), 90, Players, Timeline), []));
    }

    private static IReadOnlyList<NextRegionSample> Build(HeroPositions positions) =>
        NextRegionSampleBuilder.Build(
            new ReplayAnalysis(Match(), 90, Players, Timeline, deaths: [], wards: [], objectives: []), [positions])!;

    private static MatchId Match() => MatchId.TryCreate(1, out var id) ? id : throw new InvalidOperationException();
}
