using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Domain;

public class PlayerMatchTests
{
    [Theory]
    [InlineData(Team.Radiant, Team.Radiant, true)]
    [InlineData(Team.Radiant, Team.Dire, false)]
    [InlineData(Team.Dire, Team.Radiant, false)]
    [InlineData(Team.Dire, Team.Dire, true)]
    public void Won_ComparesPlayerTeamWithWinningTeam(Team team, Team winningTeam, bool expected)
    {
        Assert.Equal(expected, CreateMatch(team, winningTeam).Won);
    }

    [Fact]
    public void Won_IsNullWhenResultIsUnknown()
    {
        Assert.Null(CreateMatch(Team.Radiant, winningTeam: null).Won);
    }

    private static PlayerMatch CreateMatch(Team team, Team? winningTeam) =>
        new(
            MatchId: 1,
            HeroId: 1,
            Team: team,
            WinningTeam: winningTeam,
            Kda: new Kda(0, 0, 0),
            Duration: TimeSpan.FromMinutes(30),
            StartTime: DateTimeOffset.UnixEpoch);
}
