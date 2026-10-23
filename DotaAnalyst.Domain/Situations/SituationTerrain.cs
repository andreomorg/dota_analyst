namespace DotaAnalyst.Domain.Situations;

// Patch terrain and standing trees at the anchor. Null means the required map/observation is missing.
// Vision is geometric coverage by known sources, not the replay's actual visibility bit or true sight.
public sealed record SituationTerrain(
    int? Level = null,
    int? NearestSeenEnemyLevel = null,
    int? HeightAdvantage = null,
    bool? OnEnemyHighGround = null,
    int? TreesWithin300 = null,
    int? TreesWithin600 = null,
    double? TreeLineDistance = null,
    double? Openness = null,
    bool? InNoWardZone = null,
    double? ActiveRoshanPitDistance = null,
    // What the player can know from the kill announcement: dead within the minimum respawn, alive after the maximum,
    // unknown in between (the respawn is random). The boss track's real state is HiddenRoshanAlive.
    bool? RoshanAlive = null,
    bool? NearObjective = null,
    bool? NearCurrent = null,
    int? TeamWatchersActive = null,
    bool? HiddenInEnemyHeroVision = null,
    int? HiddenEnemyObserversCovering = null,
    bool? HiddenRoshanAlive = null);
