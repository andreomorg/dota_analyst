using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class PositionEstimatorTests
{
    private static readonly int[] HeroIds = [1, 2, 3, 4, 5];

    private static PositionEvidence Hero(int id, (double Safe, double Middle, double Off) lanes, int lastHits, int netWorth = 1500,
        int wards = 0, params string[] startingItems) =>
        new(id, lanes.Safe, lanes.Middle, lanes.Off, startingItems, lastHits, netWorth, wards, Profile: null);

    [Fact]
    public void ATypicalTeam_GetsEachPositionFromItsLaneAndFarm()
    {
        var estimates = PositionEstimator.Estimate(
        [
            Hero(1, (0.9, 0, 0), lastHits: 40, startingItems: "quelling_blade"),
            Hero(2, (0, 0.9, 0), lastHits: 35),
            Hero(3, (0, 0, 0.8), lastHits: 22),
            Hero(4, (0.1, 0, 0.6), lastHits: 4, wards: 2, startingItems: "ward_sentry"),
            Hero(5, (0.8, 0, 0), lastHits: 2, wards: 3, startingItems: "ward_observer"),
        ]);

        Assert.Equal([1, 2, 3, 4, 5], HeroIds.Select(id => estimates[id].Position));
        Assert.All(estimates.Values, estimate => Assert.True(estimate.Confidence > 0));
    }

    [Fact]
    public void ACarryThatDiedAndIsPoorer_StaysTheCarry_BecauseLastHitsAreNotLost()
    {
        // The safe lane at 8:00: the carry died twice (less gold than its support), but has the lane's last hits.
        var estimates = PositionEstimator.Estimate(
        [
            Hero(1, (0.9, 0, 0), lastHits: 38, netWorth: 1900),
            Hero(5, (0.9, 0, 0), lastHits: 3, netWorth: 2100, wards: 2),
        ]);

        Assert.Equal((1, 5), (estimates[1].Position, estimates[5].Position));
    }

    [Fact]
    public void AtTheHorn_TheStartingItemsAlreadySeparateCoresFromSupports()
    {
        var estimates = PositionEstimator.Estimate(
        [
            Hero(1, (0, 0, 0), lastHits: 0, startingItems: "quelling_blade"),
            Hero(5, (0, 0, 0), lastHits: 0, startingItems: ["ward_observer", "ward_sentry"]),
        ]);

        Assert.True(estimates[1].Position <= 3);
        Assert.True(estimates[5].Position >= 4);
    }

    [Fact]
    public void LaneShares_CountOnlySamplesFromTheHornToTheSecond()
    {
        // Radiant: the safe lane is the bottom one (y ≈ -6250).
        PositionSample[] samples =
        [
            new(-30, 0, 0),            // before the horn
            new(10, 2000, -6250),      // bottom lane
            new(20, 2000, -6250),
            new(30, -2000, -2000),     // middle lane (Radiant side, off the river)
            new(40, -6450, 2000),      // top lane: after the second asked
        ];

        var (safe, middle, off) = PositionEstimator.LaneShares(Team.Radiant, samples, until: 30);

        Assert.Equal((2.0 / 3, 1.0 / 3, 0.0), (safe, middle, off));
    }

    [Fact]
    public void LaneShares_UseEachTeamsSafeLane_RadiantBottomAndDireTop()
    {
        // Radiant: bottom = safe (1 and 5), top = off lane (3 and 4). Dire: the other way around.
        PositionSample[] top = [new(10, -6450, 2000)];
        PositionSample[] bottom = [new(10, 2000, -6250)];

        Assert.Equal((0.0, 0.0, 1.0), PositionEstimator.LaneShares(Team.Radiant, top, until: 10));
        Assert.Equal((1.0, 0.0, 0.0), PositionEstimator.LaneShares(Team.Radiant, bottom, until: 10));
        Assert.Equal((1.0, 0.0, 0.0), PositionEstimator.LaneShares(Team.Dire, top, until: 10));
        Assert.Equal((0.0, 0.0, 1.0), PositionEstimator.LaneShares(Team.Dire, bottom, until: 10));
    }
}
