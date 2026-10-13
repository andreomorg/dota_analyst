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
    int EnemyWardsDestroyed);
