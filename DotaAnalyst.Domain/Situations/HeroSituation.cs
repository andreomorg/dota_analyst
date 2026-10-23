using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Situations;

// Value object: a picture of one living hero at one second of the game clock, built only from what happened up to that
// second (HeroSituationBuilder). It knows nothing about what happens next: each model adds its own label on top.
// Distances are in map units; numbers per minute are the last minute already turned (MatchStatTimeline.ValueAt).
public sealed record HeroSituation(
    int HeroId,
    Team Team,
    int Seconds,
    // Position 1-5: the role queue's when the match has one; otherwise (pro matches, Turbo, unranked) estimated at this
    // very second from what happened up to it (PositionEstimator: RoleInferred = true, RoleConfidence its margin; no
    // future, unlike the 10:00 net worth RoleInference that the saved metrics of pro matches carry).
    int? Role,
    bool RoleInferred,
    double? RoleConfidence,
    int X,
    int Y,
    MapRegion Region,
    // More than MatchMetricsCalculator.RiverMargin past the river, on the other team's side.
    bool OnEnemySide,
    // Null when no ally is alive.
    int? NearestAllyDistance,
    int AlliesNear,
    int AlliesWithinFightRange,
    // Living heroes of each team besides this one (the scoreboard shows who is dead).
    int AlliesAlive,
    int EnemiesAlive,
    int Level,
    int NetWorth,
    // This hero's level minus the average level of the enemy team.
    double LevelLeadOverEnemies,
    // The team's net worth / experience minus the other team's.
    int TeamNetWorthLead,
    int TeamExperienceLead,
    // Damage this hero took from heroes in the last minute already turned.
    int DamageFromHeroesLastMinute,
    int TeamObserversActive,
    int OwnTowersLost,
    int EnemyTowersLost,
    // Since this hero's last death (a reincarnation is not one); null when it hasn't died yet.
    int? SecondsSinceLastDeath,
    // What this hero and its team did just before (ReplayActivity); all empty when the replay's activity is unknown.
    RecentActivity Recent,
    // Health and mana at this second (newer replay documents only; null otherwise).
    Condition Condition,
    // What was ready to use at this second (newer replay documents only; null otherwise).
    Readiness Readiness,
    // Roshan and the Aegis.
    MapState Map,
    // The creep waves (newer replay documents only).
    WavePosition Waves,
    // The hero's combat attributes, the fight it is in and its recent farm (newer replay documents only).
    Body Body,
    Fight Fight,
    Farm Farm,
    // What the player was telling the hero to do (newer replay documents only). It is the decision itself: a model that
    // predicts where the hero goes must not use it.
    Intent Intent,
    // The enemies missing from the team's view (newer replay documents only).
    MissingEnemies Missing,
    // What enemies were doing to the hero: disables and other debuffs (newer replay documents only).
    Afflictions Afflictions,
    // What the hero and its allies were doing for it: teleport, magic immunity, saves, heals (newer documents only).
    Boons Boons,
    // The towers around the hero and the buildings losing health (newer documents only).
    BuildingPressure Buildings,
    // The waves of the whole map (all three lanes) and their clock.
    MapWaves MapWaves,
    // Movement, buyback, smoke and ward vision.
    Surroundings Surroundings,
    // How much of the map the team's (and, hidden, the enemy's) observers see.
    VisionCoverage Vision,
    // Income by source, pings, spells traded, the rune clock.
    Signals Signals,
    // The momentum: streaks and recent kills (the scoreboard).
    Momentum Momentum,
    // What the kits can do: the enemies' disables/escapes from the draft, the hero's own disable/escape ready.
    KitState Kit,
    // How much the spells can do now: damage, healing and mana (K7).
    SpellPower Spells,
    // The neutral items: the tier open, the hero's artifact, enhancement and Madstones (N5).
    NeutralState Neutrals,
    // The matchup: what the enemies have done to the hero and its team.
    EnemyThreat Threat,
    // The direct lane opponent (role queue; inferred roles only from 10:00).
    LaneOpponent Lane,
    // What the player could not see through the fog of war: only to measure how much the visible part explains,
    // never to word a tip.
    HiddenEnemies Hidden)
{
    public SituationCourierFlight CourierFlight { get; init; } = new();
    public SituationMobileVision MobileVision { get; init; } = new();
    public SituationTerrain Terrain { get; init; } = new();
}
