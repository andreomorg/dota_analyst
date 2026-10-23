using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Domain.Situations;

// Independent of HeroSituation / model exports. A null route is unknown, not evidence of safety.
public sealed record SituationEscape(EscapeRoute? ToOwnTowerOrFountain = null, EscapeRoute? ToNearestAlly = null);

// Geometric ground route at the anchor, not a recommendation or a prediction of movement.
// HiddenNearestEnemyToPath is omniscient measurement only: never expose it as coaching advice.
public sealed record EscapeRoute(
    MapPoint Destination,
    double PathLength,
    double StraightDistance,
    double? Detour,
    double? SeenShare,
    double? NearestSeenEnemyToPath,
    double? HiddenNearestEnemyToPath);
