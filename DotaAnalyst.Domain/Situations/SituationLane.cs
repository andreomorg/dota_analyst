using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Situations;

// The hero against the enemy in the opposing role (1×3, 2×2, 4×5): melee or not (catalog; null without a profile),
// its level minus the opponent's (the scoreboard), kills each way so far, the distance while the team sees the opponent,
// and the hero damage traded with it in the last HeroSituationBuilder.LaneTradeWindowSeconds (null without hero hits).
// Hidden (the player does not know): the true distance (null when the opponent is dead) and the net worth lead.
// Everything is null when no position is known or no enemy has the opposing one.
public sealed record LaneOpponent(
    int? HeroId, bool? IAmMelee, bool? OpponentMelee, int? LevelLead, int? KillsOnMe, int? MyKillsOnThem, int? SeenDistance,
    int? DamageToThem, int? DamageFromThem, int? HiddenDistance, int? HiddenNetWorthLead)
{
    public static LaneOpponent Unknown { get; } = new(null, null, null, null, null, null, null, null, null, null, null);
}

// The hero's farm in the last HeroSituationBuilder.FarmWindowSeconds (creep last hits) and the seconds since its last
// one (null: none in the window, or the document has no creep kills).
// OwnCampsStacked/OwnCampsEmpty: the neutral camps of the team's own jungle stacked (NeutralCamp.StackedAt) or empty
// now (null without camps in the document); the enemy jungle's stacks are in HiddenEnemies.
public sealed record Farm(
    int? LaneLastHits, int? Neutrals, int? Denies, int? SecondsSinceLastCreepKill, int? OwnCampsStacked = null,
    int? OwnCampsEmpty = null)
{
    public static Farm Unknown { get; } = new(null, null, null, null);
}

// Where the hero stands against the creep waves of its lane (map progress x + y seen from its own base: positive =
// towards the enemy). Null when the hero is not in a lane or the document has no waves.
public sealed record WavePosition(
    // How far past the front of its own team's creeps (negative: behind them, the safe side).
    int? AheadOfOwnWave,
    // The front of its own team's creeps in each lane, from its own side (higher = pushed further).
    int? OwnFrontTop,
    int? OwnFrontMiddle,
    int? OwnFrontBottom,
    // The own team's creeps in the hero's lane, and those at its front (null outside a lane or before wave counts).
    int? OwnCreepsInLane = null,
    int? OwnCreepsAtFront = null,
    // Enemies the team sees in the hero's lane now (null outside a lane or without visibility); the true count is in
    // HiddenEnemies.EnemiesInMyLane.
    int? SeenEnemiesInMyLane = null,
    // Where the lane is (the hero's lane, same scale): the point between the two fronts (null unless both teams have
    // creeps there), and how far it is past the team's most advanced standing tower of the lane / short of the enemy's.
    int? ClashPoint = null,
    int? ClashPastOwnTower = null,
    int? ClashShortOfEnemyTower = null,
    // Where the lane is going: the own front now minus 10 s and 30 s ago (positive = pushing).
    int? OwnFrontChangeLast10 = null,
    int? OwnFrontChangeLast30 = null,
    // How strong the own wave in the hero's lane is (summed health at the front, a siege creep alive), and how many own
    // creeps are off every lane now (pulled or dragged; null without the data).
    int? OwnHealthAtFront = null,
    bool? OwnSiegeInLane = null,
    int? OwnCreepsOffLane = null,
    // Lane creeps of the hero's lane that died in the last HeroSituationBuilder.CreepDeathWindowSeconds: enemy creeps
    // killed by the team's heroes (pushing/farming), own creeps killed by enemy heroes, own creeps killed by an enemy
    // tower (the wave is hitting it) and enemy creeps killed by an own tower (their wave is hitting ours).
    int? EnemyCreepsKilledByTeam = null,
    int? OwnCreepsKilledByEnemyHeroes = null,
    int? OwnCreepsKilledByEnemyTower = null,
    int? EnemyCreepsKilledByOwnTower = null,
    // The own creeps in the hero's lane are at least HeroSituationBuilder.StackedWaveFactor times one wave (two waves
    // merged: a lane left alone or managed). One wave = 4 creeps + 1 every 15 min (an approximation of the spawns).
    bool? OwnWaveStacked = null,
    // At the front of the hero's lane: enemy creeps low enough to last hit and own ones to deny; whether the own wave
    // has super/mega creeps (an enemy barracks fell); lane creep hits on the hero in the last 10 s (it has aggro).
    int? LastHitsAvailable = null,
    int? DeniesAvailable = null,
    bool? OwnSuperCreeps = null,
    int? CreepHitsOnMeLast10 = null)
{
    public static WavePosition Unknown { get; } = new(null, null, null, null);
}

// The waves of the whole map from the team's side (WavePosition's scale): where each lane's waves meet and how far the
// own front moved in the last 30 s (null when the lane lacks creeps of both teams / of the team), how many lanes have
// the enemy wave at (within HeroSituationBuilder.WaveAtTowerRange of, or past) the team's most advanced standing tower
// and the team's wave at the enemy's (null without waves or building health), and the seconds until the next waves
// spawn (every 30 s from 0:00: the game clock, always known).
public sealed record MapWaves(
    int? ClashTop, int? ClashMiddle, int? ClashBottom, int? FrontChangeTop, int? FrontChangeMiddle, int? FrontChangeBottom,
    int? EnemyWavesAtOwnTowers, int? OwnWavesAtEnemyTowers, int SecondsToNextWave);
