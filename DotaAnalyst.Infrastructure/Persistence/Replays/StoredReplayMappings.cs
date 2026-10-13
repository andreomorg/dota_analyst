using DotaAnalyst.Domain.Analysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Infrastructure.Persistence.Replays;

// The JSON format of a saved replay analysis, separate from the domain model on purpose (like StoredMatchDetails):
// renaming a domain property must not silently change what is already in the database.
internal sealed record StoredReplayAnalysis(
    // Bumped when the format changes in a way old rows can't be read as-is.
    int Version,
    long MatchId,
    int EndsAtSeconds,
    IReadOnlyList<StoredLaningStats> Players,
    // Added in sprint 11 without a new Version: rows saved before have none (null) and still read.
    StoredTimeline? Timeline = null,
    // Added in sprint 12, the same way.
    IReadOnlyList<StoredHeroDeath>? Deaths = null,
    // Added in sprint 13, the same way.
    IReadOnlyList<StoredWard>? Wards = null,
    // Added in sprint 14, the same way.
    IReadOnlyList<StoredDamageRow>? Damage = null,
    // Added in sprint 15, the same way.
    IReadOnlyList<StoredItemPurchase>? Purchases = null,
    // Added in sprint 16, the same way.
    IReadOnlyList<StoredMapObjective>? Objectives = null,
    // Added in sprint 17, the same way (empty: a match without role queue).
    IReadOnlyList<StoredHeroMetrics>? Metrics = null);

internal sealed record StoredHeroMetrics(
    int HeroId, Team Team, int Role, int Deaths, int IsolatedDeaths, int DeathsOnEnemySide, double EnemySideShare,
    double AloneOnEnemySideShare, int? NetWorthAt10, int? NetWorthAt20, int LastHitsAt10, double NetWorthShare,
    int ObserversPlaced, int SentriesPlaced, int? ObserverAverageLifetimeSeconds, int WardsLostToEnemy,
    int EnemyWardsDestroyed,
    // Added in sprint 19 (null in older rows: the analysis is then outdated and parsed again).
    IReadOnlyDictionary<MapRegion, double>? TimeByRegion = null,
    IReadOnlyDictionary<MapRegion, int>? DeathsByRegion = null,
    // Added in sprint 20 (false in older rows: they all came from role queue).
    bool RoleInferred = false);

internal sealed record StoredMapObjective(
    ObjectiveKind Kind, int Seconds, Team TakenBy, int? KillerHeroId, Team? Owner, int? TowerTier, Lane? Lane,
    bool? MeleeBarracks, bool Denied, int? AegisHeroId);

internal sealed record StoredItemPurchase(int HeroId, int Seconds, string ItemKey);

internal sealed record StoredDamageRow(
    int? SourceHeroId, string SourceKind, int? TargetHeroId, string TargetKind, string? Inflictor, int Amount);

internal sealed record StoredWard(
    WardKind Kind, Team Team, int? PlacedByHeroId, int X, int Y, int PlacedAt, int? RemovedAt, bool Destroyed,
    int? DestroyedByHeroId);

internal sealed record StoredHeroDeath(
    int HeroId, int Seconds, int X, int Y, KillerKind Killer, int? KillerHeroId, bool Reincarnated,
    // Added in sprint 14 (null in older rows).
    IReadOnlyList<int>? AssistHeroIds = null);

internal sealed record StoredTimeline(IReadOnlyList<int> Seconds, IReadOnlyList<StoredHeroTimeline> Heroes);

internal sealed record StoredHeroTimeline(int HeroId, Team Team, IReadOnlyList<int> NetWorth, IReadOnlyList<int> Experience);

internal sealed record StoredLaningStats(
    int HeroId,
    Team Team,
    Lane Lane,
    int LastHits,
    int Denies,
    int NetWorth,
    int Experience,
    int Deaths);

// SavedReplayAnalysis <-> a replay_analyses row.
internal static class StoredReplayMappings
{
    public const int CurrentVersion = 1;

    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Expired = "expired";

    // Enums as text ("radiant", "bottom"): readable in the database and safe if enum values are reordered.
    // Also used by the comparison base (BaselineRepository), so a hero's metrics are the same JSON everywhere.
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static string ToStatus(SavedReplayState state) => state switch
    {
        SavedReplayState.Ready => Ready,
        SavedReplayState.Failed => Failed,
        SavedReplayState.Expired => Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown replay state."),
    };

    // Null for every state but Ready.
    public static string? ToJson(SavedReplayAnalysis result) =>
        result.Analysis is { } analysis ? JsonSerializer.Serialize(ToStored(analysis), JsonOptions) : null;

    /// <exception cref="JsonException">The JSON is malformed.</exception>
    /// <exception cref="InvalidDataException">The row can't become a result.</exception>
    public static SavedReplayAnalysis ToDomain(long matchIdValue, string status, string? data, DateTimeOffset savedAt)
    {
        if (!MatchId.TryCreate(matchIdValue, out var matchId))
        {
            throw new InvalidDataException($"A saved replay analysis has an invalid match ID ({matchIdValue}).");
        }

        return status switch
        {
            Ready => SavedReplayAnalysis.Ready(FromJson(matchId, data), savedAt),
            Failed => SavedReplayAnalysis.Failed(matchId, savedAt),
            Expired => SavedReplayAnalysis.Expired(matchId, savedAt),
            _ => throw new InvalidDataException($"A saved replay analysis has an unknown status '{status}'."),
        };
    }

    private static ReplayAnalysis FromJson(MatchId matchId, string? data)
    {
        var stored = JsonSerializer.Deserialize<StoredReplayAnalysis>(
                data ?? throw new InvalidDataException("A ready replay analysis has no data."), JsonOptions)
            ?? throw new InvalidDataException("A saved replay analysis is empty.");

        if (stored.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Saved replay analysis format version {stored.Version} is not supported.");
        }

        if (stored.MatchId != matchId.Value)
        {
            throw new InvalidDataException($"A saved replay analysis of match {matchId} says match {stored.MatchId}.");
        }

        try
        {
            return new ReplayAnalysis(
                matchId,
                stored.EndsAtSeconds,
                (stored.Players ?? []).Select(player => player is null
                        ? throw new InvalidDataException("A saved replay analysis has an empty player.")
                        : new LaningStats(player.HeroId, player.Team, player.Lane, player.LastHits, player.Denies,
                            player.NetWorth, player.Experience, player.Deaths))
                    .ToList(),
                stored.Timeline is { } timeline
                    ? new MatchTimeline(
                        timeline.Seconds ?? throw new InvalidDataException("A saved timeline has no seconds."),
                        (timeline.Heroes ?? []).Select(hero => hero is null
                                ? throw new InvalidDataException("A saved timeline has an empty hero.")
                                : new HeroTimeline(hero.HeroId, hero.Team, hero.NetWorth ?? [], hero.Experience ?? []))
                            .ToList())
                    : null,
                stored.Deaths?.Select(death => death is null
                        ? throw new InvalidDataException("A saved replay analysis has an empty death.")
                        : new HeroDeath(death.HeroId, death.Seconds, death.X, death.Y, death.Killer, death.KillerHeroId,
                            death.Reincarnated, death.AssistHeroIds))
                    .ToList(),
                stored.Wards?.Select(ward => ward is null
                        ? throw new InvalidDataException("A saved replay analysis has an empty ward.")
                        : new WardPlacement(ward.Kind, ward.Team, ward.PlacedByHeroId, ward.X, ward.Y, ward.PlacedAt,
                            ward.RemovedAt, ward.Destroyed, ward.DestroyedByHeroId))
                    .ToList(),
                stored.Damage?.Select(row => row is null
                        ? throw new InvalidDataException("A saved replay analysis has an empty damage row.")
                        : new DamageRow(row.SourceHeroId, row.SourceKind, row.TargetHeroId, row.TargetKind, row.Inflictor,
                            row.Amount))
                    .ToList(),
                stored.Purchases?.Select(purchase => purchase is null
                        ? throw new InvalidDataException("A saved replay analysis has an empty purchase.")
                        : new ItemPurchase(purchase.HeroId, purchase.Seconds, purchase.ItemKey))
                    .ToList(),
                stored.Objectives?.Select(objective => objective is null
                        ? throw new InvalidDataException("A saved replay analysis has an empty objective.")
                        : new MapObjective(objective.Kind, objective.Seconds, objective.TakenBy, objective.KillerHeroId,
                            objective.Owner, objective.TowerTier, objective.Lane, objective.MeleeBarracks, objective.Denied,
                            objective.AegisHeroId))
                    .ToList(),
                stored.Metrics?.Select(hero => hero is null
                        ? throw new InvalidDataException("A saved replay analysis has empty metrics.")
                        : ToDomain(hero))

                    .ToList());
        }
        catch (ArgumentException ex)
        {
            // The domain refused the saved values.
            throw new InvalidDataException($"A saved replay analysis breaks a rule: {ex.Message}", ex);
        }
    }

    private static StoredReplayAnalysis ToStored(ReplayAnalysis analysis) =>
        new(
            Version: CurrentVersion,
            MatchId: analysis.MatchId.Value,
            EndsAtSeconds: analysis.EndsAtSeconds,
            Players: analysis.Players
                .Select(player => new StoredLaningStats(
                    player.HeroId, player.Team, player.Lane, player.LastHits, player.Denies,
                    player.NetWorth, player.Experience, player.Deaths))
                .ToList(),
            Timeline: analysis.Timeline is { } timeline
                ? new StoredTimeline(
                    timeline.Seconds,
                    timeline.Heroes
                        .Select(hero => new StoredHeroTimeline(hero.HeroId, hero.Team, hero.NetWorth, hero.Experience))
                        .ToList())
                : null,
            Deaths: analysis.Deaths?
                .Select(death => new StoredHeroDeath(death.HeroId, death.Seconds, death.X, death.Y, death.Killer,
                    death.KillerHeroId, death.Reincarnated, death.AssistHeroIds))
                .ToList(),
            Wards: analysis.Wards?
                .Select(ward => new StoredWard(ward.Kind, ward.Team, ward.PlacedByHeroId, ward.X, ward.Y, ward.PlacedAt,
                    ward.RemovedAt, ward.Destroyed, ward.DestroyedByHeroId))
                .ToList(),
            Damage: analysis.Damage?
                .Select(row => new StoredDamageRow(row.SourceHeroId, row.SourceKind, row.TargetHeroId, row.TargetKind,
                    row.Inflictor, row.Amount))
                .ToList(),
            Purchases: analysis.Purchases?
                .Select(purchase => new StoredItemPurchase(purchase.HeroId, purchase.Seconds, purchase.ItemKey))
                .ToList(),
            Objectives: analysis.Objectives?
                .Select(objective => new StoredMapObjective(objective.Kind, objective.Seconds, objective.TakenBy,
                    objective.KillerHeroId, objective.Owner, objective.TowerTier, objective.Lane, objective.MeleeBarracks,
                    objective.Denied, objective.AegisHeroId))
                .ToList(),
            Metrics: analysis.Metrics?
                .Select(ToStored)

                .ToList());


    // Shared with the comparison base (BaselineRepository): the same JSON for a hero's metrics.
    public static HeroMetrics ToDomain(StoredHeroMetrics hero) =>
        new(hero.HeroId, hero.Team, hero.Role, hero.Deaths, hero.IsolatedDeaths, hero.DeathsOnEnemySide,
            hero.EnemySideShare, hero.AloneOnEnemySideShare, hero.NetWorthAt10, hero.NetWorthAt20, hero.LastHitsAt10,
            hero.NetWorthShare, hero.ObserversPlaced, hero.SentriesPlaced, hero.ObserverAverageLifetimeSeconds,
            hero.WardsLostToEnemy, hero.EnemyWardsDestroyed,
            hero.TimeByRegion is { } time ? PerRegion<double>.Of(time.GetValueOrDefault) : null,
            hero.DeathsByRegion is { } deaths ? PerRegion<int>.Of(deaths.GetValueOrDefault) : null,
            hero.RoleInferred);

    public static StoredHeroMetrics ToStored(HeroMetrics hero) =>
        new(hero.HeroId, hero.Team, hero.Role, hero.Deaths, hero.IsolatedDeaths, hero.DeathsOnEnemySide,
            hero.EnemySideShare, hero.AloneOnEnemySideShare, hero.NetWorthAt10, hero.NetWorthAt20, hero.LastHitsAt10,
            hero.NetWorthShare, hero.ObserversPlaced, hero.SentriesPlaced, hero.ObserverAverageLifetimeSeconds,
            hero.WardsLostToEnemy, hero.EnemyWardsDestroyed,
            hero.TimeByRegion is { } time ? ByRegion(time) : null,
            hero.DeathsByRegion is { } deaths ? ByRegion(deaths) : null,
            hero.RoleInferred);
    // Saved as { "OwnBase": 0.1, ... }: readable in the database and safe if a region is added later.
    private static Dictionary<MapRegion, T> ByRegion<T>(PerRegion<T> values) =>
        Enum.GetValues<MapRegion>().ToDictionary(region => region, region => values[region]);
}
