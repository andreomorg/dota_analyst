using System.Text.Json;
using System.Text.Json.Serialization;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Infrastructure.OpenDota;

namespace DotaAnalyst.Infrastructure.Persistence.Matches;

// The JSON format of a saved match. It is separate from the domain model on purpose: renaming a domain
// property must not silently change (or break reading) what is already in the database.
internal sealed record StoredMatchDetails(
    // Bumped when the format changes in a way old rows can't be read as-is.
    int Version,
    long MatchId,
    double DurationSeconds,
    DateTimeOffset StartTime,
    Team? WinningTeam,
    int RadiantScore,
    int DireScore,
    bool IsParsed,
    IReadOnlyList<StoredMatchPlayer> Players,
    // Added later: rows saved before it have no "replayUrl" and are read with null, so Version stays 1.
    string? ReplayUrl = null);

internal sealed record StoredMatchPlayer(
    long? AccountId,
    int HeroId,
    Team Team,
    int Kills,
    int Deaths,
    int Assists,
    int LastHits,
    int Denies,
    int GoldPerMinute,
    int ExperiencePerMinute,
    int NetWorth,
    int HeroDamage,
    int TowerDamage,
    int HeroHealing,
    int Level,
    IReadOnlyList<int> ItemIds,
    int? NeutralItemId,
    IReadOnlyList<StoredBenchmark> Benchmarks);

internal sealed record StoredBenchmark(BenchmarkMetric Metric, double Value, double Percentile);

// Domain <-> stored JSON, like OpenDotaMappings does for OpenDota's JSON.
internal static class StoredMatchMappings
{
    public const int CurrentVersion = 1;

    // Enums as text ("radiant", "goldPerMinute"): readable in the database and safe if enum values are reordered.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string ToJson(MatchDetails match) => JsonSerializer.Serialize(ToStored(match), JsonOptions);

    /// <exception cref="JsonException">The JSON is malformed.</exception>
    /// <exception cref="InvalidDataException">The JSON is valid but can't become a match.</exception>
    public static MatchDetails FromJson(string json)
    {
        var stored = JsonSerializer.Deserialize<StoredMatchDetails>(json, JsonOptions)
            ?? throw new InvalidDataException("A saved match is empty.");
        return ToDomain(stored);
    }

    private static StoredMatchDetails ToStored(MatchDetails match) =>
        new(
            Version: CurrentVersion,
            MatchId: match.MatchId.Value,
            DurationSeconds: match.Duration.TotalSeconds,
            StartTime: match.StartTime,
            WinningTeam: match.WinningTeam,
            RadiantScore: match.RadiantScore,
            DireScore: match.DireScore,
            IsParsed: match.IsParsed,
            Players: match.Players.Select(ToStored).ToList(),
            ReplayUrl: match.ReplayUrl?.AbsoluteUri);

    private static StoredMatchPlayer ToStored(MatchPlayer player) =>
        new(
            AccountId: player.AccountId?.Value,
            HeroId: player.HeroId,
            Team: player.Team,
            Kills: player.Kda.Kills,
            Deaths: player.Kda.Deaths,
            Assists: player.Kda.Assists,
            LastHits: player.LastHits,
            Denies: player.Denies,
            GoldPerMinute: player.GoldPerMinute,
            ExperiencePerMinute: player.ExperiencePerMinute,
            NetWorth: player.NetWorth,
            HeroDamage: player.HeroDamage,
            TowerDamage: player.TowerDamage,
            HeroHealing: player.HeroHealing,
            Level: player.Level,
            ItemIds: player.ItemIds,
            NeutralItemId: player.NeutralItemId,
            Benchmarks: player.Benchmarks.Select(b => new StoredBenchmark(b.Metric, b.Value, b.Percentile)).ToList());

    private static MatchDetails ToDomain(StoredMatchDetails stored)
    {
        if (stored.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Saved match format version {stored.Version} is not supported.");
        }

        if (!MatchId.TryCreate(stored.MatchId, out var matchId))
        {
            throw new InvalidDataException($"A saved match has an invalid match ID ({stored.MatchId}).");
        }

        return new MatchDetails(
            MatchId: matchId,
            Duration: TimeSpan.FromSeconds(stored.DurationSeconds),
            StartTime: stored.StartTime,
            WinningTeam: stored.WinningTeam,
            RadiantScore: stored.RadiantScore,
            DireScore: stored.DireScore,
            IsParsed: stored.IsParsed,
            Players: (stored.Players ?? []).Select(ToDomain).ToList(),
            // Same rule as for OpenDota's address: anything that isn't absolute http(s) becomes null.
            ReplayUrl: OpenDotaMappings.ToReplayUrl(stored.ReplayUrl));
    }

    private static MatchPlayer ToDomain(StoredMatchPlayer stored) =>
        new(
            AccountId: ToAccountId(stored.AccountId),
            HeroId: stored.HeroId,
            Team: stored.Team,
            Kda: new Kda(stored.Kills, stored.Deaths, stored.Assists),
            LastHits: stored.LastHits,
            Denies: stored.Denies,
            GoldPerMinute: stored.GoldPerMinute,
            ExperiencePerMinute: stored.ExperiencePerMinute,
            NetWorth: stored.NetWorth,
            HeroDamage: stored.HeroDamage,
            TowerDamage: stored.TowerDamage,
            HeroHealing: stored.HeroHealing,
            Level: stored.Level,
            ItemIds: stored.ItemIds ?? [],
            NeutralItemId: stored.NeutralItemId,
            Benchmarks: (stored.Benchmarks ?? []).Select(ToDomain).ToList());

    private static Benchmark ToDomain(StoredBenchmark stored)
    {
        if (stored.Percentile is < 0 or > 1)
        {
            throw new InvalidDataException($"A saved benchmark has an invalid percentile ({stored.Percentile}).");
        }

        return new Benchmark(stored.Metric, stored.Value, stored.Percentile);
    }

    private static AccountId? ToAccountId(long? value)
    {
        if (value is null)
        {
            return null;
        }

        return AccountId.TryCreate(value.Value, out var accountId)
            ? accountId
            : throw new InvalidDataException($"A saved player has an invalid account ID ({value}).");
    }
}
