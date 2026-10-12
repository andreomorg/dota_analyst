using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using static DotaAnalyst.Tests.Domain.ReplayBuilder;

namespace DotaAnalyst.Tests.Domain;

public class ReplayAnalysisTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(420)] // the match ended before 10:00
    [InlineData(ReplayAnalysis.LaningPhaseEndsAtSeconds)]
    public void Create_AcceptsTimesUpToTheEndOfTheLaningPhase(int endsAtSeconds)
    {
        var replay = new ReplayAnalysis(MatchIdOf(10), endsAtSeconds, [Hero(31, Team.Radiant, Lane.Bottom)]);

        Assert.Equal(endsAtSeconds, replay.EndsAtSeconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ReplayAnalysis.LaningPhaseEndsAtSeconds + 1)]
    public void Create_RejectsTimesOutsideTheLaningPhase(int endsAtSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayAnalysis(MatchIdOf(10), endsAtSeconds, []));
    }

    [Fact]
    public void Create_RejectsAnInvalidMatchId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayAnalysis(default, 600, []));
    }

    [Fact]
    public void Create_RejectsTheSameHeroTwice()
    {
        // Players are matched by hero ID, so it must be unique.
        Assert.Throws<ArgumentException>(() => Analysis(
            10, Hero(31, Team.Radiant, Lane.Bottom), Hero(31, Team.Dire, Lane.Top)));
    }

    [Fact]
    public void Create_KeepsItsOwnCopyOfThePlayers()
    {
        var players = new List<LaningStats> { Hero(31, Team.Radiant, Lane.Bottom) };
        var replay = new ReplayAnalysis(MatchIdOf(10), 600, players);

        players.Add(Hero(1, Team.Dire, Lane.Top));

        Assert.Single(replay.Players);
    }

    [Fact]
    public void FindHero_FindsByHeroId()
    {
        var replay = Analysis(10, Hero(31, Team.Radiant, Lane.Bottom), Hero(1, Team.Dire, Lane.Top));

        Assert.Equal(Team.Dire, replay.FindHero(1)?.Team);
        Assert.Null(replay.FindHero(2));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]    // hero ID must be positive
    [InlineData(31, -1, 0, 0, 0, 0)]
    [InlineData(31, 0, -1, 0, 0, 0)]
    [InlineData(31, 0, 0, -1, 0, 0)]
    [InlineData(31, 0, 0, 0, -1, 0)]
    [InlineData(31, 0, 0, 0, 0, -1)]
    public void LaningStats_RejectsInvalidNumbers(int heroId, int lastHits, int denies, int netWorth, int experience, int deaths)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LaningStats(heroId, Team.Radiant, Lane.Top, lastHits, denies, netWorth, experience, deaths));
    }
}
