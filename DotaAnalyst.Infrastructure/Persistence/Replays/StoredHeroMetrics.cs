using System.Text.Json;
using System.Text.Json.Serialization;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Infrastructure.Persistence.Replays;

internal sealed record StoredHeroMetrics(
    int HeroId, Team Team, int Role, int Deaths, int IsolatedDeaths, int DeathsOnEnemySide, double EnemySideShare,
    double AloneOnEnemySideShare, int? NetWorthAt10, int? NetWorthAt20, int LastHitsAt10, double NetWorthShare,
    int ObserversPlaced, int SentriesPlaced, int? ObserverAverageLifetimeSeconds, int WardsLostToEnemy,
    int EnemyWardsDestroyed,
    // Added in sprint 19 (null in older rows: the analysis is then outdated and parsed again).
    IReadOnlyDictionary<MapRegion, double>? TimeByRegion = null,
    IReadOnlyDictionary<MapRegion, int>? DeathsByRegion = null,
    // Added in sprint 20 (false in older rows: they all came from role queue).
    bool RoleInferred = false,
    // Added for estimated movement metrics; null identifies rows that predate these metrics.
    double? EstimatedMovementDistance = null,
    IReadOnlyDictionary<MapRegion, double>? EstimatedMovementByRegion = null,
    int? MovementSampleSegments = null,
    // Added after movement metrics; absent in older JSON, which remains readable and triggers a refresh.
    StoredRegionPhases<double>? TimeByRegionByPhase = null,
    StoredRegionPhases<int>? DeathsByRegionByPhase = null,
    // Fights, kill participation and quick re-deaths (null in older rows: the analysis is then parsed again).
    int? TeamFights = null,
    int? FightsNearby = null,
    double? KillParticipation = null,
    int? QuickRedeaths = null,
    int? LaneDamageDealt = null,
    int? LaneDamageTaken = null,
    double? TeamObserverUptime = null,
    double? LeadLostPerDeath = null,
    IReadOnlyDictionary<MapRegion, double>? NetWorthGainByRegion = null,
    IReadOnlyDictionary<MapRegion, int>? MinutesByRegion = null,
    // Added after the first-item timing metric; null in older metric rows.
    int? FirstBigItemSeconds = null,
    // Terrain metrics (MP8); absent in older rows, which stay valid (never parsed again for it).
    StoredTerrainMetrics? Terrain = null,
    // Vision metrics; absent in older rows, which stay valid.
    StoredVisionMetrics? Vision = null,
    // Map objective metrics; absent in older rows, which stay valid.
    StoredMapObjectiveMetrics? Objectives = null,
    // Teleport metrics; absent in older rows, which stay valid.
    TeleportMetrics? Teleports = null,
    // Play quality metrics; absent in older rows, which stay valid.
    PlayQualityMetrics? Plays = null);

internal sealed record StoredMapObjectiveMetrics(
    MapObjectiveCounts? Counts, int RoshanKills, int? RoshanKillsNearby, int TormentorKills, int? TormentorKillsNearby,
    int EnemyTowersTaken, int? EnemyTowersTakenNearby, int? OwnShrinesLost = null, int? EnemyShrinesTaken = null);

internal sealed record StoredVisionMetrics(
    double SeenByEnemiesShare, int? DeathsWhileTracked, int? DeathsUnseenBefore, int? DeathsToKillerFromFog, int? KillsFromFog,
    double? ObserverSeenShare = null);

internal sealed record StoredTerrainMetrics(
    double? OwnHighGroundShare, double? EnemyHighGroundShare, double? RiverLevelShare, double? NarrowGroundShare,
    double? DeepEnemyJungleShare, int DeathsOnEnemyHighGround, int DeathsBelowEnemyHighGround, int? DeathsInNarrowGround,
    int? DeathsInDeepEnemyJungle);

internal sealed record StoredRegionPhases<T>(
    IReadOnlyDictionary<MapRegion, T> EarlyGame,
    IReadOnlyDictionary<MapRegion, T> MidGame,
    IReadOnlyDictionary<MapRegion, T> LateGame);
