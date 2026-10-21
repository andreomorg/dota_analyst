using DotaAnalyst.Domain.DeathRisk;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class DeathRiskSampleBuilderTests
{
    private static readonly LaningStats[] Players =
    [
        new(1, Team.Radiant, Lane.Bottom, 0, 0, 600, 0, 0),
        new(6, Team.Dire, Lane.Top, 0, 0, 600, 0, 0),
    ];

    // The match ends at 1:00.
    private static readonly MatchTimeline Timeline = new(
        [0, 60],
        [new HeroTimeline(1, Team.Radiant, [600, 700], [0, 100]), new HeroTimeline(6, Team.Dire, [600, 700], [0, 100])]);

    [Fact]
    public void OneSampleEveryTenSeconds_UntilTheLastFullWindow()
    {
        var samples = Build(deaths: [], AliveFrom(1, 0, 60));

        // 0, 10 ... 40 (40 + 15 is before the end at 60; 50 + 15 would pass it).
        Assert.Equal([0, 10, 20, 30, 40], samples.Select(sample => sample.Situation.Seconds));
        Assert.All(samples, sample => Assert.False(sample.DiesSoon));
    }

    [Theory]
    [InlineData(18, false)] // before the sample
    [InlineData(21, true)]
    [InlineData(35, true)]  // the last second of the window
    [InlineData(36, false)]
    public void Label_IsADeathInTheNextFifteenSeconds(int diedAt, bool diesSoon)
    {
        var samples = Build([Death(diedAt, reincarnated: false)], AliveFrom(1, 0, 60));

        var at20 = samples.Single(sample => sample.Situation.Seconds == 20);

        Assert.Equal(diesSoon, at20.DiesSoon);
        Assert.Equal(diesSoon ? diedAt : (int?)null, at20.DiedAtSeconds);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(20)]
    public void DyingAtTheSamplesSecond_GivesNoSample(int diedAt)
    {
        var samples = Build([Death(diedAt, reincarnated: true)], AliveFrom(1, 0, 60));

        Assert.DoesNotContain(samples, sample => sample.Situation.Seconds == 20);
    }

    [Fact]
    public void Reincarnation_CountsAsDying()
    {
        var samples = Build([Death(25, reincarnated: true)], AliveFrom(1, 0, 60));

        Assert.True(samples.Single(sample => sample.Situation.Seconds == 20).DiesSoon);
    }

    [Fact]
    public void DeadHero_GivesNoSample()
    {
        // Dead from 12 to 31: no sample at 20 or 30.
        var positions = new HeroPositions(1, Enumerable.Range(0, 31).Select(i => i * 2)
            .Where(second => second is <= 12 or >= 32)
            .Select(second => new PositionSample(second, 0, 0)).ToList());

        var samples = Build([Death(13, reincarnated: false)], positions);

        Assert.Equal([0, 10, 40], samples.Select(sample => sample.Situation.Seconds));
        Assert.True(samples.Single(sample => sample.Situation.Seconds == 10).DiesSoon);
    }

    [Fact]
    public void OutdatedAnalysis_HasNoSamples()
    {
        var replay = new ReplayAnalysis(Match(), 60, Players, Timeline);

        Assert.Null(DeathRiskSampleBuilder.Build(replay, []));
    }

    private static IReadOnlyList<DeathRiskSample> Build(IReadOnlyList<HeroDeath> deaths, HeroPositions positions) =>
        DeathRiskSampleBuilder.Build(
            new ReplayAnalysis(Match(), 60, Players, Timeline, deaths, wards: [], objectives: []), [positions])!;

    private static HeroDeath Death(int seconds, bool reincarnated) =>
        new(1, seconds, 0, 0, KillerKind.Hero, 6, reincarnated);

    // A sample every 2 s from `from` to `to`.
    private static HeroPositions AliveFrom(int heroId, int from, int to) =>
        new(heroId, Enumerable.Range(0, (to - from) / 2 + 1).Select(i => new PositionSample(from + i * 2, 0, 0)).ToList());

    private static MatchId Match() => MatchId.TryCreate(1, out var id) ? id : throw new InvalidOperationException();
}
