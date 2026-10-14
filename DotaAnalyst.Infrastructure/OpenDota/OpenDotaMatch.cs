using System.Text.Json.Serialization;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Only the fields we use from GET /matches/{match_id}.
// Anything OpenDota may send as null (or omit) is nullable and gets a default in OpenDotaMappings,
// so one odd field doesn't make the whole match fail to deserialize.
public record OpenDotaMatch(
    long MatchId,
    int? Duration,
    long? StartTime,
    bool? RadiantWin,
    int? RadiantScore,
    int? DireScore,
    // Parser version: null when the replay was not parsed.
    int? Version,
    IReadOnlyList<OpenDotaMatchPlayer>? Players,
    // Where Valve keeps the replay file (e.g. http://replay202.valve.net/570/<match>_<salt>.dem.bz2).
    string? ReplayUrl = null);

// item_0..item_5 are named explicitly: the naming policy would not put an underscore before the digit.
public record OpenDotaMatchPlayer(
    // Null for players with an anonymous (private) profile.
    long? AccountId,
    int PlayerSlot,
    int? HeroId,
    int? Kills,
    int? Deaths,
    int? Assists,
    int? LastHits,
    int? Denies,
    int? GoldPerMin,
    int? XpPerMin,
    int? NetWorth,
    int? HeroDamage,
    int? TowerDamage,
    int? HeroHealing,
    int? Level,
    [property: JsonPropertyName("item_0")] int? Item0,
    [property: JsonPropertyName("item_1")] int? Item1,
    [property: JsonPropertyName("item_2")] int? Item2,
    [property: JsonPropertyName("item_3")] int? Item3,
    [property: JsonPropertyName("item_4")] int? Item4,
    [property: JsonPropertyName("item_5")] int? Item5,
    int? ItemNeutral,
    // Keys are metric names (gold_per_min, xp_per_min...), so a dictionary instead of a fixed class.
    IReadOnlyDictionary<string, OpenDotaBenchmark>? Benchmarks,
    IReadOnlyDictionary<string, int>? DamageInflictorReceived = null,
    IReadOnlyDictionary<string, int>? AbilityUses = null,
    IReadOnlyDictionary<string, int>? ItemUses = null,
    int? ActionsPerMin = null,
    IReadOnlyDictionary<string, int>? Actions = null,
    IReadOnlyDictionary<string, int>? GoldReasons = null,
    IReadOnlyDictionary<string, int>? XpReasons = null);

// Raw = the player's value; Pct = percentile (0 to 1) against other players of the same hero.
public record OpenDotaBenchmark(double? Raw, double? Pct);
