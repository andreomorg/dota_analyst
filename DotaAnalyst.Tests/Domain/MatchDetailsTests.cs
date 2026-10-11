using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Domain;

public class MatchDetailsTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public void MatchId_TryCreate_AcceptsOnlyPositiveIds(long value, bool expected)
    {
        Assert.Equal(expected, MatchId.TryCreate(value, out _));
    }

    [Fact]
    public void FindPlayer_ReturnsPlayerWithAccountIdOrNull()
    {
        var me = MatchPlayerBuilder.Create(accountId: MatchPlayerBuilder.Account(99517715));
        var anonymous = MatchPlayerBuilder.Create(accountId: null);
        var match = CreateMatch(Team.Radiant, me, anonymous);

        Assert.Same(me, match.FindPlayer(MatchPlayerBuilder.Account(99517715)));
        Assert.Null(match.FindPlayer(MatchPlayerBuilder.Account(1)));
    }

    [Theory]
    [InlineData(Team.Radiant, Team.Radiant, true)]
    [InlineData(Team.Dire, Team.Radiant, false)]
    public void Won_ComparesPlayerTeamWithWinningTeam(Team playerTeam, Team winningTeam, bool expected)
    {
        var player = MatchPlayerBuilder.Create(team: playerTeam);

        Assert.Equal(expected, CreateMatch(winningTeam, player).Won(player));
    }

    [Fact]
    public void Won_IsNullWhenResultIsUnknown()
    {
        var player = MatchPlayerBuilder.Create();

        Assert.Null(CreateMatch(winningTeam: null, player).Won(player));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Benchmark_RejectsPercentileOutsideZeroToOne(double percentile)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Benchmark(BenchmarkMetric.GoldPerMinute, 400, percentile));
    }

    private static MatchDetails CreateMatch(Team? winningTeam, params MatchPlayer[] players)
    {
        MatchId.TryCreate(1, out var matchId);
        return new MatchDetails(
            matchId, TimeSpan.FromMinutes(30), DateTimeOffset.UnixEpoch, winningTeam, 10, 5, false, players);
    }
}
