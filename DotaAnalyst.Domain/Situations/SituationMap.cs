using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Situations;

// Roshan and the Aegis at this second, from the objectives every analysis has (the game announces them to everyone).
public sealed record MapState(
    // Since the last Roshan kill; null before the first.
    int? SecondsSinceRoshanKill,
    // Who holds an Aegis now (taken in the last HeroSituationBuilder.AegisSeconds and not used up by a reincarnation).
    bool HasAegis,
    bool AllyHasAegis,
    bool EnemyHasAegis,
    // Towers (tiers 1-3) each team lost in each lane: where the map is open.
    LaneTowers OwnTowersLost,
    LaneTowers EnemyTowersLost,
    // Night shortens every hero's vision (null: the document has no day/night).
    bool? IsNight = null);

// Around the hero at this second: its speed over the last position step and the part of it towards the enemy base
// (units/s; null after a teleport/blink or without a previous sample), whether it could buy back now (gold on hand vs
// the cost, and the cooldown; null without gold or buybacks), whether it and how many living allies are under a smoke
// (null without buffs), and whether a standing observer of its team sees it.
// AlliesWithTeleportReady: living allies with a teleport off cooldown (Readiness.TeleportReady; null without inventories):
// the help that can arrive. The player sees the allies' items on the scoreboard.
// FacingEnemyBase / FacingNearestSeenEnemy: the cosine (-1..1) between where the hero faces and the direction to the
// enemy base (the diagonal) / to the nearest enemy the team sees (null without facing, or no such enemy). StashItems:
// items bought and still in the stash (slots 9-14). Couriers alive per team (the kill feed shows courier kills).
// Damage taken from heroes in the last 6 s by type and healing received / done then (null without the data).
public sealed record Surroundings(
    int? MoveSpeed, int? AdvanceSpeed, bool? BuybackReady, bool? UnderSmoke, int? AlliesUnderSmoke, bool InOwnObserverVision,
    int? AlliesWithTeleportReady = null, double? FacingEnemyBase = null, double? FacingNearestSeenEnemy = null,
    int? StashItems = null, int? OwnCouriersAlive = null, int? EnemyCouriersAlive = null, int? PhysicalTakenLast6 = null,
    int? MagicalTakenLast6 = null, int? PureTakenLast6 = null, int? HealingReceivedLast6 = null, int? HealingDoneLast6 = null);

// The share (0..1) of the team's own jungle, the enemy jungle and the river that its standing observers see, and
// (hidden: the player does not know where enemy wards are) of its own jungle that the enemy's observers see.
public sealed record VisionCoverage(double OwnJungle, double EnemyJungle, double River, double HiddenEnemyInOwnJungle);

// Around the hero in the last moments: its gold in the last HeroSituationBuilder.EconomyWindowSeconds by source (lane
// creeps, neutrals, heroes, buildings/Roshan) and its XP (null without the economy data); teammates' pings within
// PingNearRadius of it and its own pings in the last SignalWindowSeconds (null without pings); ability casts whose
// target the replay linked — enemies' on it and its own on enemies (null without targets); and the seconds until the
// next power rune spawns (the clock).
public sealed record Signals(
    int? GoldFromCreeps, int? GoldFromNeutrals, int? GoldFromHeroes, int? GoldFromObjectives, int? ExperienceGained,
    int? AllyPingsNear, int? OwnPings, int? EnemySpellsOnMe, int? MySpellsOnEnemies, int SecondsToNextPowerRune);

// The standing towers nearest to the hero (its own and the enemy's): how far and their health share; and how many of the
// team's buildings lost health in the last HeroSituationBuilder.BuildingAttackWindowSeconds (the game warns the team).
// Null when the document has no building health. The enemy's buildings under attack are in HiddenEnemies.
public sealed record BuildingPressure(
    int? NearestOwnTowerDistance, double? NearestOwnTowerHealthShare, int? NearestEnemyTowerDistance,
    double? NearestEnemyTowerHealthShare, int? OwnBuildingsUnderAttack)
{
    public static BuildingPressure Unknown { get; } = new(null, null, null, null, null);
}

public sealed record LaneTowers(int Top, int Middle, int Bottom);
