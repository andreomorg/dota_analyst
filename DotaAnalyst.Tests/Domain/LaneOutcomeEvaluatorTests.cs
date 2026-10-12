using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using static DotaAnalyst.Tests.Domain.ReplayBuilder;

namespace DotaAnalyst.Tests.Domain;

public class LaneOutcomeEvaluatorTests
{
    [Theory]
    [InlineData(5000, LaneOutcome.Won)]
    [InlineData(1000, LaneOutcome.Won)]   // exactly on the threshold
    [InlineData(999, LaneOutcome.Even)]
    [InlineData(0, LaneOutcome.Even)]
    [InlineData(-999, LaneOutcome.Even)]
    [InlineData(-1000, LaneOutcome.Lost)] // exactly on the threshold
    [InlineData(-5000, LaneOutcome.Lost)]
    public void Rate_UsesThreshold(int netWorthDifference, LaneOutcome expected)
    {
        Assert.Equal(1000, LaneOutcomeEvaluator.NetWorthLeadToWin);
        Assert.Equal(expected, LaneOutcomeEvaluator.Rate(netWorthDifference));
    }

    [Fact]
    public void Evaluate_ComparesBothSidesOfTheSamePhysicalLane()
    {
        // Radiant bottom (safe lane) against Dire bottom (off lane); the other lanes don't count.
        var replay = Analysis(
            10,
            Hero(31, Team.Radiant, Lane.Bottom, netWorth: 2000),
            Hero(1, Team.Radiant, Lane.Bottom, netWorth: 3000),
            Hero(2, Team.Radiant, Lane.Top, netWorth: 9000),
            Hero(28, Team.Dire, Lane.Bottom, netWorth: 2500),
            Hero(3, Team.Dire, Lane.Bottom, netWorth: 1250),
            Hero(4, Team.Dire, Lane.Middle, netWorth: 9000));

        var lane = LaneOutcomeEvaluator.Evaluate(replay, heroId: 31);

        Assert.NotNull(lane);
        Assert.Equal(31, lane.Player.HeroId);
        Assert.Equal([1], lane.Allies.Select(hero => hero.HeroId));
        Assert.Equal([28, 3], lane.Opponents.Select(hero => hero.HeroId)); // richest first
        Assert.Equal(2000 + 3000 - 2500 - 1250, lane.NetWorthDifference);
        Assert.Equal(LaneOutcome.Won, lane.Outcome);
    }

    [Theory]
    [InlineData(1000, LaneOutcome.Lost)]
    [InlineData(2000, LaneOutcome.Even)]
    [InlineData(3000, LaneOutcome.Won)]
    public void Evaluate_SoloLane(int playerNetWorth, LaneOutcome expected)
    {
        var replay = Analysis(
            10,
            Hero(17, Team.Dire, Lane.Middle, playerNetWorth),
            Hero(11, Team.Radiant, Lane.Middle, netWorth: 2000));

        var lane = LaneOutcomeEvaluator.Evaluate(replay, heroId: 17);

        Assert.Equal(expected, lane!.Outcome);
        Assert.Equal(playerNetWorth - 2000, lane.NetWorthDifference);
        Assert.Empty(lane.Allies);
    }

    [Theory]
    [InlineData(Lane.Jungle)]
    [InlineData(Lane.Roaming)]
    public void Evaluate_OutsideTheLanes_HasNoOutcome(Lane lane)
    {
        var replay = Analysis(
            10,
            Hero(31, Team.Radiant, lane),
            Hero(1, Team.Radiant, lane),
            Hero(2, Team.Dire, lane));

        var evaluation = LaneOutcomeEvaluator.Evaluate(replay, heroId: 31);

        Assert.NotNull(evaluation);
        Assert.Equal(lane, evaluation.Player.Lane);
        Assert.Null(evaluation.Outcome);
        Assert.Null(evaluation.NetWorthDifference);
        Assert.Empty(evaluation.Allies);
        Assert.Empty(evaluation.Opponents);
    }

    [Fact]
    public void Evaluate_FreeLane_HasNoOutcome()
    {
        var replay = Analysis(
            10,
            Hero(31, Team.Radiant, Lane.Top),
            Hero(1, Team.Radiant, Lane.Top),
            Hero(2, Team.Dire, Lane.Bottom));

        var lane = LaneOutcomeEvaluator.Evaluate(replay, heroId: 31);

        Assert.NotNull(lane);
        Assert.Null(lane.Outcome);
        Assert.Null(lane.NetWorthDifference);
        Assert.Equal([1], lane.Allies.Select(hero => hero.HeroId));
        Assert.Empty(lane.Opponents);
    }

    [Fact]
    public void Evaluate_HeroNotInTheReplay_IsNull()
    {
        var replay = Analysis(10, Hero(1, Team.Radiant, Lane.Top));

        Assert.Null(LaneOutcomeEvaluator.Evaluate(replay, heroId: 31));
    }
}
