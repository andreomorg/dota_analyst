using System.Text.Json;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Infrastructure.OpenDota;

namespace DotaAnalyst.Tests.Infrastructure;

// Uses a real response of GET /matches/9034320024 (trimmed to the fields we read),
// so the tests break if our DTOs stop matching what OpenDota actually sends.
public class OpenDotaMatchMappingsTests
{
    private const long UserAccountId = 99517715;

    [Fact]
    public void ToMatchDetails_MapsMatchFields()
    {
        // Act
        var details = LoadFixture().ToMatchDetails();

        // Assert
        Assert.Equal(9034320024, details.MatchId.Value);
        Assert.Equal(TimeSpan.FromSeconds(1725), details.Duration);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791422972), details.StartTime);
        Assert.Equal(Team.Radiant, details.WinningTeam);
        Assert.Equal(60, details.RadiantScore);
        Assert.Equal(22, details.DireScore);
        Assert.Equal(10, details.Players.Count);
    }

    [Fact]
    public void ToMatchDetails_UnparsedMatch_IsNotParsed()
    {
        // This match was never parsed: OpenDota doesn't send "version" at all.
        var match = LoadFixture();
        Assert.Null(match.Version);

        Assert.False(match.ToMatchDetails().IsParsed);
        Assert.True((match with { Version = 21 }).ToMatchDetails().IsParsed);
    }

    [Fact]
    public void ToMatchDetails_SplitsPlayersIntoTeamsBySlot()
    {
        var details = LoadFixture().ToMatchDetails();

        Assert.Equal(5, details.Players.Count(player => player.Team == Team.Radiant));
        Assert.Equal(5, details.Players.Count(player => player.Team == Team.Dire));
    }

    [Fact]
    public void ToMatchDetails_MapsUserPlayer()
    {
        // Act
        var details = LoadFixture().ToMatchDetails();
        var player = details.FindPlayer(CreateAccountId(UserAccountId));

        // Assert
        Assert.NotNull(player);
        Assert.Equal(31, player.HeroId); // Lich
        Assert.Equal(Team.Radiant, player.Team);
        Assert.True(details.Won(player));
        Assert.Equal(new Kda(2, 4, 19), player.Kda);
        Assert.Equal(358, player.GoldPerMinute);
        // item_5 is 0 (empty slot) and is dropped.
        Assert.Equal([214, 1, 254, 36, 60], player.ItemIds);
        Assert.Equal(1599, player.NeutralItemId);
    }

    [Fact]
    public void ToMatchDetails_DropsEmptyItemSlots()
    {
        var players = LoadFixture().ToMatchDetails().Players;

        Assert.All(players, player => Assert.DoesNotContain(0, player.ItemIds));
        // Slot 129 has an empty item_0 in the real data.
        Assert.Equal([77, 77, 102, 36, 63], players.Single(player => player.HeroId == 68).ItemIds);
    }

    [Fact]
    public void ToMatchDetails_MapsAllBenchmarks()
    {
        var player = LoadFixture().ToMatchDetails().FindPlayer(CreateAccountId(UserAccountId))!;

        Assert.Equal(Enum.GetValues<BenchmarkMetric>().Length, player.Benchmarks.Count);
        var gold = player.Benchmarks.Single(benchmark => benchmark.Metric == BenchmarkMetric.GoldPerMinute);
        Assert.Equal(358, gold.Value);
        Assert.Equal(0.51, gold.Percentile, precision: 2);
    }

    [Fact]
    public void ToMatchPlayer_AnonymousOrInvalidAccount_HasNullAccountId()
    {
        var player = LoadFixture().Players![0];

        Assert.Null((player with { AccountId = null }).ToMatchPlayer().AccountId);
        Assert.Null((player with { AccountId = 0 }).ToMatchPlayer().AccountId);
        // Valve's placeholder ID for anonymous players.
        Assert.Null((player with { AccountId = uint.MaxValue }).ToMatchPlayer().AccountId);
    }

    [Fact]
    public void ToMatchPlayer_IgnoresUnknownBenchmarksAndNullPercentiles()
    {
        var player = LoadFixture().Players![0] with
        {
            Benchmarks = new Dictionary<string, OpenDotaBenchmark>
            {
                ["gold_per_min"] = new(Raw: 500, Pct: 0.7),
                ["xp_per_min"] = new(Raw: 600, Pct: null),
                ["stuns_per_min"] = new(Raw: 1, Pct: 0.5),
                ["tower_damage"] = new(Raw: 9000, Pct: 1.2),
            },
        };

        var benchmarks = player.ToMatchPlayer().Benchmarks;

        Assert.Equal(2, benchmarks.Count);
        Assert.Contains(new Benchmark(BenchmarkMetric.GoldPerMinute, 500, 0.7), benchmarks);
        // Out-of-range percentiles are clamped instead of breaking the whole match.
        Assert.Contains(new Benchmark(BenchmarkMetric.TowerDamage, 9000, 1), benchmarks);
    }

    [Fact]
    public void ToMatchPlayer_NullFields_UseDefaults()
    {
        var player = new OpenDotaMatchPlayer(
            AccountId: null, PlayerSlot: 130, HeroId: 5, Kills: null, Deaths: null, Assists: null,
            LastHits: null, Denies: null, GoldPerMin: null, XpPerMin: null, NetWorth: null,
            HeroDamage: null, TowerDamage: null, HeroHealing: null, Level: null,
            Item0: null, Item1: 0, Item2: 48, Item3: null, Item4: null, Item5: null,
            ItemNeutral: 0, Benchmarks: null);

        var mapped = player.ToMatchPlayer();

        Assert.Equal(Team.Dire, mapped.Team);
        Assert.Equal(new Kda(0, 0, 0), mapped.Kda);
        Assert.Equal([48], mapped.ItemIds);
        Assert.Null(mapped.NeutralItemId);
        Assert.Empty(mapped.Benchmarks);
    }

    [Theory]
    [InlineData(true, Team.Radiant)]
    [InlineData(false, Team.Dire)]
    [InlineData(null, null)]
    public void ToMatchDetails_MapsRadiantWinToWinningTeam(bool? radiantWin, Team? expected)
    {
        Assert.Equal(expected, (LoadFixture() with { RadiantWin = radiantWin }).ToMatchDetails().WinningTeam);
    }

    [Fact]
    public void ToMatchDetails_MapsReplayUrl()
    {
        var details = LoadFixture().ToMatchDetails();

        Assert.Equal(new Uri("http://replay202.valve.net/570/9034320024_96247495.dem.bz2"), details.ReplayUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("replay202.valve.net/570/1_2.dem.bz2")] // not absolute
    [InlineData("ftp://replay202.valve.net/570/1_2.dem.bz2")]
    [InlineData("file:///etc/passwd")]
    public void ToMatchDetails_InvalidReplayUrl_BecomesNull(string? replayUrl)
    {
        Assert.Null((LoadFixture() with { ReplayUrl = replayUrl }).ToMatchDetails().ReplayUrl);
    }

    internal static OpenDotaMatch LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Fixtures", "match-9034320024.json");
        return JsonSerializer.Deserialize<OpenDotaMatch>(File.ReadAllText(path), OpenDotaClient.JsonOptions)!;
    }

    private static AccountId CreateAccountId(long value)
    {
        Assert.True(AccountId.TryCreate(value, out var accountId));
        return accountId;
    }
}
