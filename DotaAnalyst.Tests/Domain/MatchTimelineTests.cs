using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class MatchTimelineTests
{
    private static HeroTimeline Hero(int heroId, Team team, params int[] netWorth) =>
        new(heroId, team, netWorth, netWorth.Select(value => value / 2).ToList());

    [Fact]
    public void LeadOf_SumsEachTeamAndSubtractsTheOther()
    {
        var timeline = new MatchTimeline(
            [0, 60, 95],
            [Hero(1, Team.Radiant, 600, 900, 1500), Hero(2, Team.Radiant, 600, 800, 1000), Hero(3, Team.Dire, 600, 1000, 3000)]);

        var radiant = TimelineEvaluator.LeadOf(timeline, Team.Radiant);
        var dire = TimelineEvaluator.LeadOf(timeline, Team.Dire);

        Assert.Equal([600, 700, -500], radiant.NetWorth);
        Assert.Equal([300, 350, -250], radiant.Experience);
        Assert.Equal([-600, -700, 500], dire.NetWorth); // the same lead, seen from the other side
    }

    [Theory]
    [InlineData(new int[0])] // no point
    [InlineData(new[] { -60, 0 })] // before 0:00
    [InlineData(new[] { 0, 60, 60 })] // not going forward
    public void Seconds_MustStartAtZeroOrLaterAndGoForward(int[] seconds)
    {
        Assert.Throws<ArgumentException>(() => new MatchTimeline(seconds, []));
    }

    [Fact]
    public void Heroes_NeedOneValuePerPointAndAreUnique()
    {
        Assert.Throws<ArgumentException>(() => new MatchTimeline([0, 60], [Hero(1, Team.Radiant, 600)]));
        Assert.Throws<ArgumentException>(() =>
            new MatchTimeline([0], [Hero(1, Team.Radiant, 600), Hero(1, Team.Dire, 600)]));
    }

    [Fact]
    public void HeroTimeline_RejectsNegativeOrMismatchedValues()
    {
        Assert.Throws<ArgumentException>(() => new HeroTimeline(1, Team.Radiant, [-1], [0]));
        Assert.Throws<ArgumentException>(() => new HeroTimeline(1, Team.Radiant, [0, 1], [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeroTimeline(0, Team.Radiant, [0], [0]));
    }
}
