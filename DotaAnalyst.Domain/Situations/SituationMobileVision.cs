namespace DotaAnalyst.Domain.Situations;

// Moving sources are 128-unit groups from the replay, not individual unit counts. Coverage samples walkable ground
// in FightRange on a 256-unit lattice; it is vision by known sources, not actual visibility or true sight.
public sealed record SituationMobileVision(
    double? TeamCoverage = null,
    int? TeamSourcesNear = null,
    bool? HiddenInEnemyMobileRange = null);
