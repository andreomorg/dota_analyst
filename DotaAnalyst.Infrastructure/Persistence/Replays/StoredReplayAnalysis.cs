using System.Text.Json;
using System.Text.Json.Serialization;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

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
    IReadOnlyList<StoredHeroMetrics>? Metrics = null,
    // Added with the heatmaps, the same way.
    IReadOnlyList<StoredHeatmap>? Heatmaps = null,
    // Added when the match page began showing the player's skill build; older rows are re-read from the replay.
    IReadOnlyList<StoredAbilityUpgrade>? AbilityUpgrades = null,
    // Added with the neutral crafts (N6), the same way (rows saved before have none and are NOT re-read for it).
    IReadOnlyList<StoredNeutralCraft>? NeutralCrafts = null,
    IReadOnlyList<StoredNeutralProgress>? NeutralProgress = null);

internal sealed record StoredLaningStats(
    int HeroId,
    Team Team,
    Lane Lane,
    int LastHits,
    int Denies,
    int NetWorth,
    int Experience,
    int Deaths);

internal sealed record StoredTimeline(IReadOnlyList<int> Seconds, IReadOnlyList<StoredHeroTimeline> Heroes);

// OtherSeries: the statistics saved per minute besides net worth and experience (StatCatalog), keyed by name
// ("lastHits"); absent in rows saved before them (the analysis is then outdated and parsed again).
internal sealed record StoredHeroTimeline(
    int HeroId, Team Team, IReadOnlyList<int> NetWorth, IReadOnlyList<int> Experience,
    IReadOnlyDictionary<StatKey, IReadOnlyList<int>>? OtherSeries = null);

internal sealed record StoredHeroDeath(
    int HeroId, int Seconds, int X, int Y, KillerKind Killer, int? KillerHeroId, bool Reincarnated,
    // Added in sprint 14 (null in older rows).
    IReadOnlyList<int>? AssistHeroIds = null);

internal sealed record StoredWard(
    WardKind Kind, Team Team, int? PlacedByHeroId, int X, int Y, int PlacedAt, int? RemovedAt, bool Destroyed,
    int? DestroyedByHeroId);

internal sealed record StoredDamageRow(
    int? SourceHeroId, string SourceKind, int? TargetHeroId, string TargetKind, string? Inflictor, int Amount);

internal sealed record StoredItemPurchase(int HeroId, int Seconds, string ItemKey);

internal sealed record StoredMapObjective(
    ObjectiveKind Kind, int Seconds, Team TakenBy, int? KillerHeroId, Team? Owner, int? TowerTier, Lane? Lane,
    bool? MeleeBarracks, bool Denied, int? AegisHeroId);

internal sealed record StoredAbilityUpgrade(int HeroId, int Seconds, string AbilityName, int Level);

// Each phase as a flat list [column, row, samples, column, row, samples, ...]: a match's heatmaps are about 30
// lists of a few hundred cells, and objects per cell would triple the size of the row.
// Proximity: [near, grouped] of the early, mid and late game, flat (6 numbers); absent in rows saved before it was
// counted (the analysis is then outdated and parsed again).
internal sealed record StoredHeatmap(
    int HeroId, IReadOnlyList<int> EarlyGame, IReadOnlyList<int> MidGame, IReadOnlyList<int> LateGame,
    IReadOnlyList<int>? Proximity = null);

internal sealed record StoredNeutralCraft(int HeroId, double Seconds, string Artifact, int? ArtifactLevel, int Tier, string? Enhancement,
    int? EnhancementLevel);

internal sealed record StoredNeutralProgress(int HeroId, int Acquired, int Found, int InHandAtEnd, int NeutralGold);
