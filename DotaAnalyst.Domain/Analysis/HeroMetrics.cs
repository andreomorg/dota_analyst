using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Analysis;

// The numbers of one hero in one match that show where to improve, computed from the replay by the
// MatchMetricsCalculator. The same numbers will be collected for many matches to compare with other players of the
// same hero (plan.md, stage 11).
public record HeroMetrics(
    int HeroId,
    Team Team,
    // Role-queue position, 1 (carry) to 5 (hard support).
    int Role,
    // Positioning: deaths (Aegis deaths included: the mistake happened), deaths with no ally hero nearby, deaths on
    // the enemy's side of the map.
    int Deaths,
    int IsolatedDeaths,
    int DeathsOnEnemySide,
    // Share (0..1) of the time alive from 0:00 on the enemy's side of the map, and on it with no ally nearby.
    double EnemySideShare,
    double AloneOnEnemySideShare,
    // Economy: net worth at 10:00 and 20:00 (null when the match ended before), last hits at the end of the laning
    // phase, and share (0..1) of the team's net worth at the end.
    int? NetWorthAt10,
    int? NetWorthAt20,
    int LastHitsAt10,
    double NetWorthShare,
    // Vision: wards placed, how long the observers lived on average (null without observers), own wards the enemy
    // destroyed, enemy wards destroyed.
    int ObserversPlaced,
    int SentriesPlaced,
    int? ObserverAverageLifetimeSeconds,
    int WardsLostToEnemy,
    int EnemyWardsDestroyed,
    // Positioning by map region (MapRegions): share (0..1) of the time alive from 0:00 in each region, and deaths in
    // each. Null for metrics computed before the regions existed.
    PerRegion<double>? TimeByRegion = null,
    PerRegion<int>? DeathsByRegion = null,
    // The match had no role queue (pro matches): Role came from RoleInference. Good for the comparison base, not for
    // the player's own analysis.
    bool RoleInferred = false,
    // Estimated straight-line displacement between adjacent alive position samples, in map units. A gap longer than
    // the replay's 2-second sample interval is skipped; teleports/blinks may still appear as displacement.
    double? EstimatedMovementDistance = null,
    // The segment's midpoint determines its region; this is an estimate, not the exact path traveled.
    PerRegion<double>? EstimatedMovementByRegion = null,
    // Number of adjacent sample pairs used; nullable so older stored metrics can be identified and refreshed.
    int? MovementSampleSegments = null,
    // Position samples by map region within each game-clock phase. Time values are shares of alive samples in that
    // phase; deaths are counts. Null for metrics computed before phase splits existed.
    PerGamePhase<PerRegion<double>>? TimeByRegionByPhase = null,
    PerGamePhase<PerRegion<int>>? DeathsByRegionByPhase = null,
    // Fights (TeamfightDetector) of the match, and how many of them the hero was near (FightPresenceRadius of the
    // fight's center, from a little before the first death to the last). Null for metrics computed before them.
    int? TeamFights = null,
    int? FightsNearby = null,
    // (kills + combat-log assists) / enemy hero deaths, 0..1; null when no enemy died. The combat log credits fewer
    // assists than the scoreboard: consistent across matches, lower than the in-game number.
    double? KillParticipation = null,
    // Deaths within QuickRedeathSeconds of coming back from the previous death.
    int? QuickRedeaths = null,
    // Damage dealt to and taken from enemy heroes in the laning phase (minutes 0-9 of the game clock). Null for metrics
    // computed before them.
    int? LaneDamageDealt = null,
    int? LaneDamageTaken = null,
    // Share (0..1) of the match with at least one observer of the hero's team on the map (the team's, not only the
    // hero's: vision is shared). Null for metrics computed before it.
    double? TeamObserverUptime = null,
    // Average fall of the team's net worth lead from the minute of each of the hero's deaths to the next minute
    // (positive = lead lost). Correlation, not cause: the whole fight moves the lead. Null without deaths that have a
    // next minute (Aegis deaths left out).
    double? LeadLostPerDeath = null,
    // Net worth gained in the minutes the hero spent mostly in each region, and how many minutes those were (rate =
    // gain / minutes). Minutes the hero was dead all along are left out. Null for metrics computed before them.
    PerRegion<double>? NetWorthGainByRegion = null,
    PerRegion<int>? MinutesByRegion = null);
