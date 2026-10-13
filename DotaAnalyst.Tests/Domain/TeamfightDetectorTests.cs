using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class TeamfightDetectorTests
{
    // Heroes 1-5 are Radiant, 6-10 Dire.
    private static readonly IReadOnlyDictionary<int, Team> Teams =
        Enumerable.Range(1, 10).ToDictionary(hero => hero, hero => hero <= 5 ? Team.Radiant : Team.Dire);

    private static HeroDeath Death(int heroId, int seconds) => new(heroId, seconds, 0, 0, KillerKind.Hero, null, false);

    [Fact]
    public void DeathsCloseTogether_AreOneFight_WonByTheTeamThatLostFewer()
    {
        var fight = Assert.Single(TeamfightDetector.Detect(
            [Death(6, 600), Death(1, 610), Death(7, 625), Death(8, 640)], Teams));

        Assert.Equal((600, 640, 1, 3, Team.Radiant), (fight.StartSeconds, fight.EndSeconds, fight.RadiantDeaths, fight.DireDeaths, fight.Winner));
        Assert.Equal(4, fight.Deaths.Count);
    }

    [Fact]
    public void AGapLongerThan15Seconds_SplitsFights()
    {
        var fights = TeamfightDetector.Detect(
            [Death(1, 100), Death(6, 110), Death(2, 115), Death(7, 131), Death(8, 140), Death(3, 146)], Teams);

        Assert.Equal([(100, 115), (131, 146)], fights.Select(fight => (fight.StartSeconds, fight.EndSeconds)));
    }

    [Fact]
    public void FewerThanThreeDeaths_IsNotAFight()
    {
        Assert.Empty(TeamfightDetector.Detect([Death(1, 100), Death(6, 105), Death(2, 300)], Teams));
    }

    [Fact]
    public void EqualDeaths_HaveNoWinner_AndOrderDoesNotMatter()
    {
        var fight = Assert.Single(TeamfightDetector.Detect(
            [Death(7, 512), Death(1, 500), Death(6, 505), Death(2, 520)], Teams));

        Assert.Null(fight.Winner);
        Assert.Equal(500, fight.StartSeconds);
    }

    [Fact]
    public void HeroDeath_WithTheSameAssists_IsEqual()
    {
        var a = new HeroDeath(1, 10, 0, 0, KillerKind.Hero, 6, false, [8, 7]);
        var b = new HeroDeath(1, 10, 0, 0, KillerKind.Hero, 6, false, [7, 8, 8]);

        Assert.Equal(a, b);
        Assert.Throws<ArgumentException>(() => new HeroDeath(1, 10, 0, 0, KillerKind.Hero, 6, false, [1]));
    }
}
