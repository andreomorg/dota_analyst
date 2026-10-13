using DotaAnalyst.Domain.Analysis;

namespace DotaAnalyst.Tests.Domain;

public class LaneMatchupTests
{
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(4, 5)]
    [InlineData(5, 4)]
    public void OpposingRole_ReturnsThePositionOnTheOtherSideOfTheSameLane(int role, int expected) =>
        Assert.Equal(expected, LaneMatchup.OpposingRole(role));

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void OpposingRole_ReturnsNullForAnInvalidPosition(int role) =>
        Assert.Null(LaneMatchup.OpposingRole(role));
}
