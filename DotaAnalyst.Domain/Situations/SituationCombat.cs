using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Situations;

// The hero's combat attributes at this second (HeroCombatStats; the player sees them on the HUD). Armor is estimated
// from the base armor and the agility (items and buffs left out); null when the document has no stats.
public sealed record Body(
    double? EstimatedArmor, double? MagicResistance, int? MoveSpeed, double? AverageDamage, double? Strength,
    double? Agility, double? Intellect)
{
    public static Body Unknown { get; } = new(null, null, null, null, null, null, null);
}

// The hero's health and mana (0..1 of the maximum, plus the health itself: 300 is low for anyone), the lowest health
// share among the allies within fight range, the gold in hand, whether the hero was stunned, silenced or invisible, and
// the enemies its team saw (the game's fog of war) — all things the player sees on screen. Null when unknown.
public sealed record Condition(
    int? Health, double? HealthShare, double? ManaShare, double? LowestAllyHealthShare, int? Gold = null,
    bool? Stunned = null, bool? Silenced = null, bool? Invisible = null,
    // Enemies the team saw at this second: anywhere, near, within fight range, and the nearest one's distance.
    int? VisibleEnemies = null, int? VisibleEnemiesNear = null, int? VisibleEnemiesWithinFightRange = null,
    int? NearestVisibleEnemyDistance = null)
{
    public static Condition Unknown { get; } = new(null, null, null, null);
}

// The fight around the hero, from the hero-vs-hero hits (finished 2 s buckets only): how many different enemies hit it
// and how many it hit in the last HeroSituationBuilder.HitWindowSeconds, and whether it struck first in the last
// HeroSituationBuilder.FightStartWindowSeconds (null: no hits then, or both sides in the same first bucket).
public sealed record Fight(int? EnemiesHittingMe, int? EnemiesIHit, bool? IStruckFirst)
{
    public static Fight Unknown { get; } = new(null, null, null);
}

// The matchup as the player knows it at this second: the most kills one enemy got on the hero so far (the scoreboard),
// and about the nearest enemy the team sees — its kills on the hero and on the team, its level minus the hero's (shown
// over its head) and the disables it put on the team so far (null without debuffs). The nearest-enemy numbers are null
// when no enemy is seen (or the document has no visibility). The draft (known from the pick screen): how many living
// enemies have each catalog role and how many are melee; null when the catalog lacks a profile of any enemy.
public sealed record EnemyThreat(
    int MostKillsOnMeByOneEnemy, int? NearestVisibleEnemyKillsOnMe, int? NearestVisibleEnemyKillsOnTeam,
    int? NearestVisibleEnemyLevelLead, int? NearestVisibleEnemyDisablesOnTeam,
    int? EnemyDisablersAlive = null, int? EnemyInitiatorsAlive = null, int? EnemyNukersAlive = null,
    int? EnemyEscapesAlive = null, int? EnemyMeleeAlive = null);

// Where the enemy heroes were, read from the replay (omniscient: the player did not necessarily see them), whether they
// had just used a smoke (seconds ago; null as in RecentActivity) and their average health share within fight range.
public sealed record HiddenEnemies(
    int? NearestEnemyDistance, int EnemiesNear, int EnemiesWithinFightRange, int? EnemySmokeSecondsAgo = null,
    double? EnemiesHealthShare = null,
    // The enemy team's scan (the scanned team does not see it) and a power rune an enemy took.
    int? EnemyScanSecondsAgo = null,
    int? EnemyPowerRuneSecondsAgo = null,
    // How far the enemy creeps of the hero's lane are ahead of it (WavePosition's scale; the fog hides them).
    int? EnemyWaveAhead = null,
    // How many enemy creeps are at the front of that wave.
    int? EnemyCreepsAtFront = null,
    // The own wave's health at the front minus the enemy wave's (who wins the lane's creep fight).
    int? WaveHealthBalanceAtFront = null,
    // The enemy creeps in the hero's lane make a stacked wave (WavePosition.OwnWaveStacked's rule).
    bool? EnemyWaveStacked = null,
    // Stacked neutral camps in the enemy's jungle (the player sees them only with vision there).
    int? EnemyCampsStacked = null,
    // The enemy's buildings that lost health in the last 10 s (the team only sees it with vision there).
    int? EnemyBuildingsUnderAttack = null,
    // Living enemies in the hero's lane now, seen or not (null outside a lane).
    int? EnemiesInMyLane = null,
    // A standing enemy observer sees the hero; living enemies under a smoke; living enemies able to buy back.
    bool? InEnemyObserverVision = null,
    int? EnemiesUnderSmoke = null,
    int? EnemiesWithBuyback = null,
    // Living enemies with a teleport ready (the player cannot know their cooldowns).
    int? EnemiesWithTeleportReady = null,
    // A power rune lies on the map now (the player sees it only with vision at the spot).
    bool? PowerRuneOnMap = null,
    // Big items (Momentum's rule) the enemies completed in the last 2 minutes (their power spikes).
    int? EnemyBigItemsLast2Minutes = null,
    // Enemies within fight range with a disable ready (a learned ability or an item off cooldown; the KIT part).
    int? EnemiesWithDisableReadyNear = null,
    // Enemies within fight range with their ultimate ready / a ready Black King Bar.
    int? EnemiesUltimateReadyNear = null,
    int? EnemiesBlackKingBarReadyNear = null,
    // The enemy team saw this hero (the player does not know it for sure).
    bool? SeenByEnemies = null);

// Living enemies the team has not seen for at least HeroSituationBuilder.MissingAfterSeconds (the game's fog of war;
// the minimap keeps showing where they were last seen): how many, and for the one last seen closest to this hero, the
// distance to that spot and how long ago it was (both null when no enemy is missing, or it was never seen within
// HeroSituationBuilder.LastSeenLookbackSeconds). Null when the document has no visibility.
public sealed record MissingEnemies(int? Count, int? NearestLastSeenDistance, int? NearestLastSeenSecondsAgo)
{
    public static MissingEnemies Unknown { get; } = new(null, null, null);
}

// The momentum from the scoreboard: the hero's kills since its last death (its streak), its deaths in the last
// HeroSituationBuilder.RecentDeathsWindowSeconds, the kills of its team and of the enemy team in the last minute, and how
// many enemies have reached level 6 (an ultimate to use).
// SecondsSinceMyBigItem: since the hero's last item of HeroSituationBuilder.BigItemCost or more (a power spike; null
// without the item catalog or before its first one).
public sealed record Momentum(
    int KillStreak, int RecentDeaths, int TeamKillsLastMinute, int EnemyKillsLastMinute, int EnemiesWithUltimateLevel,
    int? SecondsSinceMyBigItem = null);
