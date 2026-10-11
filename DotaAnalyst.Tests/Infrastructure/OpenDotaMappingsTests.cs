using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.OpenDota;

namespace DotaAnalyst.Tests.Infrastructure;

public class OpenDotaMappingsTests
{
    [Fact]
    public void ToPlayerMatch_MapsAllFields()
    {
        // Arrange
        var match = CreateMatch(playerSlot: 131, radiantWin: false);

        // Act
        var playerMatch = match.ToPlayerMatch();

        // Assert
        Assert.Equal(8770401770, playerMatch.MatchId);
        Assert.Equal(75, playerMatch.HeroId);
        Assert.Equal(Team.Dire, playerMatch.Team);
        Assert.Equal(Team.Dire, playerMatch.WinningTeam);
        Assert.Equal(new Kda(4, 8, 10), playerMatch.Kda);
        Assert.Equal(TimeSpan.FromSeconds(1455), playerMatch.Duration);
        Assert.Equal(new DateTimeOffset(2026, 4, 13, 17, 31, 2, TimeSpan.Zero), playerMatch.StartTime);
    }

    [Theory]
    [InlineData(true, Team.Radiant)]
    [InlineData(false, Team.Dire)]
    [InlineData(null, null)]
    public void ToPlayerMatch_MapsRadiantWinToWinningTeam(bool? radiantWin, Team? expected)
    {
        Assert.Equal(expected, CreateMatch(playerSlot: 0, radiantWin).ToPlayerMatch().WinningTeam);
    }

    private static OpenDotaRecentMatch CreateMatch(int playerSlot, bool? radiantWin) =>
        new(
            MatchId: 8770401770,
            PlayerSlot: playerSlot,
            RadiantWin: radiantWin,
            Duration: 1455,
            HeroId: 75,
            StartTime: 1776101462,
            Kills: 4,
            Deaths: 8,
            Assists: 10);
}
