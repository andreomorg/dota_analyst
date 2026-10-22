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
    HiddenEnemies Hidden);

// Seconds since something was used (null: not in the last HeroSituationBuilder.RecentWindowSeconds, or never). A use
// says it happened, not that the item is now on cooldown: the recharge is unknown here.
public sealed record RecentActivity(
    // A Smoke of Deceit used by this hero's team (the player sees the team's smoke).
    int? OwnSmokeSecondsAgo,
    // This hero's last teleport (scroll or boots of travel).
    int? TeleportSecondsAgo,
    // This hero's last escape or save item (Blink, Force Staff, Glimmer Cape, Black King Bar...; HeroSituationBuilder).
    int? EscapeItemSecondsAgo,
    // How many items and abilities this hero used in the last 30 s: how busy the moment was.
    int ItemUsesLast30Seconds,
    int AbilityUsesLast30Seconds,
    // Damage taken from and dealt to enemy heroes in the last HeroSituationBuilder.HitWindowSeconds (finished 2 s
    // buckets only); null for documents without combat buckets.
    int? DamageFromHeroesRecently = null,
    int? DamageToHeroesRecently = null,
    // This hero's buyback within HeroSituationBuilder.BuybackCooldownSeconds (it then has no buyback), the team's glyph
    // within GlyphCooldownSeconds, the team's scan, and a power rune this hero took (last PowerRuneWindowSeconds).
    int? OwnBuybackSecondsAgo = null,
    int? OwnGlyphSecondsAgo = null,
    int? OwnScanSecondsAgo = null,
    int? OwnPowerRuneSecondsAgo = null)
{
    public static RecentActivity Unknown { get; } = new(null, null, null, 0, 0);
}

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

// The hero's combat attributes at this second (HeroCombatStats; the player sees them on the HUD). Armor is estimated
// from the base armor and the agility (items and buffs left out); null when the document has no stats.
public sealed record Body(
    double? EstimatedArmor, double? MagicResistance, int? MoveSpeed, double? AverageDamage, double? Strength,
    double? Agility, double? Intellect)
{
    public static Body Unknown { get; } = new(null, null, null, null, null, null, null);
}

// The fight around the hero, from the hero-vs-hero hits (finished 2 s buckets only): how many different enemies hit it
// and how many it hit in the last HeroSituationBuilder.HitWindowSeconds, and whether it struck first in the last
// HeroSituationBuilder.FightStartWindowSeconds (null: no hits then, or both sides in the same first bucket).
public sealed record Fight(int? EnemiesHittingMe, int? EnemiesIHit, bool? IStruckFirst)
{
    public static Fight Unknown { get; } = new(null, null, null);
}

// The player's orders in the last HeroSituationBuilder.IntentWindowSeconds (HeroOrders of the hero's samples then): how
// many moves, attacks and casts, and the last move destination — how far from the hero, how much further towards the
// enemy base it is (map progress from the own side, positive = forward) and whether it is on the enemy side. Null when
// the document has no orders (the destination ones also when there was no move order).
public sealed record Intent(
    int? Moves, int? Attacks, int? Casts, int? DestinationDistance, int? DestinationAdvance, bool? DestinationOnEnemySide)
{
    public static Intent Unknown { get; } = new(null, null, null, null, null, null);
}

// Living enemies the team has not seen for at least HeroSituationBuilder.MissingAfterSeconds (the game's fog of war;
// the minimap keeps showing where they were last seen): how many, and for the one last seen closest to this hero, the
// distance to that spot and how long ago it was (both null when no enemy is missing, or it was never seen within
// HeroSituationBuilder.LastSeenLookbackSeconds). Null when the document has no visibility.
public sealed record MissingEnemies(int? Count, int? NearestLastSeenDistance, int? NearestLastSeenSecondsAgo)
{
    public static MissingEnemies Unknown { get; } = new(null, null, null);
}

// The enemy debuffs on the hero (Debuff, DebuffKinds): how many disables (stun, hex, silence, root), slows and others
// were on it at this second, the seconds it spent disabled in the last HeroSituationBuilder.AfflictionWindowSeconds, and
// how many different enemies put something on it then. Null when the document has no debuffs.
public sealed record Afflictions(int? Disables, int? Slows, int? Others, double? DisabledSeconds, int? EnemiesAfflicting)
{
    public static Afflictions Unknown { get; } = new(null, null, null, null, null);
}

// The buffs on the hero at this second (Buff, BuffKinds): channelling a teleport, magic immune, under a save (Glimmer,
// Eul's, Aeon Disk...), how many heals and speed buffs, and how many different allies put a buff on it in the last
// HeroSituationBuilder.AfflictionWindowSeconds. Null when the document has no buffs.
public sealed record Boons(bool? Teleporting, bool? MagicImmune, bool? Saved, int? Heals, int? SpeedBuffs, int? AlliesBuffing)
{
    public static Boons Unknown { get; } = new(null, null, null, null, null, null);
}

// The standing towers nearest to the hero (its own and the enemy's): how far and their health share; and how many of the
// team's buildings lost health in the last HeroSituationBuilder.BuildingAttackWindowSeconds (the game warns the team).
// Null when the document has no building health. The enemy's buildings under attack are in HiddenEnemies.
public sealed record BuildingPressure(
    int? NearestOwnTowerDistance, double? NearestOwnTowerHealthShare, int? NearestEnemyTowerDistance,
    double? NearestEnemyTowerHealthShare, int? OwnBuildingsUnderAttack)
{
    public static BuildingPressure Unknown { get; } = new(null, null, null, null, null);
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

public sealed record LaneTowers(int Top, int Middle, int Bottom);

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

// The momentum from the scoreboard: the hero's kills since its last death (its streak), its deaths in the last
// HeroSituationBuilder.RecentDeathsWindowSeconds, the kills of its team and of the enemy team in the last minute, and how
// many enemies have reached level 6 (an ultimate to use).
// SecondsSinceMyBigItem: since the hero's last item of HeroSituationBuilder.BigItemCost or more (a power spike; null
// without the item catalog or before its first one).
public sealed record Momentum(
    int KillStreak, int RecentDeaths, int TeamKillsLastMinute, int EnemyKillsLastMinute, int EnemiesWithUltimateLevel,
    int? SecondsSinceMyBigItem = null);

// The kits (abilities of the current patch, AbilityEffects): living enemies whose kit has a stun / a silence or hex / an
// escape / invisibility (known from the draft; null without the abilities' catalog), and whether the hero has a disable
// / an escape (escape, invisibility, save or spell immunity) ready now, from a learned ability off cooldown or an item in
// its inventory off cooldown (null without the data).
public sealed record KitState(
    int? EnemiesWithStun, int? EnemiesWithSilenceOrHex, int? EnemiesWithEscape, int? EnemiesWithInvisibility,
    bool? MyDisableReady, bool? MyEscapeReady);

// The neutral items at this second (7.38+: crafted from Madstones, one tier at a time). Visible: the tier open (the game
// clock and the build's rules), the hero's own artifact, enhancement, Madstones and crafts. Hidden: the enemies'.
// Null where the build's neutral rules (NeutralRules), the slots or the crafts/Madstones (documents from before N2) are
// missing.
public sealed record NeutralState(
    // The highest tier open now and the seconds until the next opens (null when every tier is open).
    int? TierOpen,
    int? SecondsToNextTier,
    // The tier of the artifact in the neutral slot (0 = none), how many tiers behind the open one, its active ready.
    int? MyArtifactTier,
    int? MyTiersBehind,
    bool? MyArtifactActiveReady,
    // The enhancement's level (0 = none), from the last craft.
    int? MyEnhancementLevel,
    // Madstones in hand; whether they pay for the open tier not crafted yet; seconds since the last craft.
    int? MyMadstones,
    bool? CanCraftNow,
    int? SecondsSinceLastCraft,
    // Hidden: enemies within fight range with an active artifact ready, the living enemies' Madstones in hand, and how
    // many tiers behind the open one the enemies' artifacts are on average.
    int? HiddenEnemyArtifactActivesReadyNear,
    int? HiddenEnemyMadstones,
    double? HiddenEnemyTiersBehind)
{
    public static NeutralState Unknown { get; } = new(null, null, null, null, null, null, null, null, null, null, null, null);
}

// How much the spells can do at this second, in hit points and mana (AbilityDetails.MainAmount: one headline number
// per spell; passives and percentages left out). "Ready" = learned up to now, no cooldown running and enough mana.
// Null when the abilities' data, the upgrades, the cooldowns or the mana are missing.
public sealed record SpellPower(
    // The hero's own ready spells: damage and healing summed, their mana cost, and the mana left after casting them all
    // (negative = cannot cast them all now).
    int? MyReadyDamage,
    int? MyReadyHealing,
    int? MyReadyManaCost,
    int? MyManaAfterReadySpells,
    // Allies within fight range: the healing their ready spells can give (ally cooldowns are on screen).
    int? AlliesReadyHealingNear,
    // Living enemies: the damage of their learned spells at their levels, cooldowns and mana ignored (the levels can be
    // seen by selecting the hero).
    int? EnemyKitDamageAlive,
    // Hidden: the enemies within fight range, with their real cooldowns and mana.
    int? HiddenEnemiesReadyDamageNear,
    int? HiddenEnemiesReadyHealingNear,
    // The hero's own ready spells by shape and reach (talents learned and Scepter/Shard bought up to now applied):
    // how many hit an area, how many hit one unit, the longest cast range, and how many disables reach the nearest
    // enemy the team sees (range plus area).
    int? MyReadyAreaSpells = null,
    int? MyReadySingleTargetSpells = null,
    int? MyReadySpellMaxRange = null,
    int? MyReadyDisablesReachingSeenEnemy = null,
    // The living enemies' learned disables (visible: levels and talents can be inspected): the longest reach, how many
    // a basic dispel cannot remove, how many go through spell immunity (Black King Bar does not stop them).
    int? EnemyKitDisableMaxRange = null,
    int? EnemyKitUndispellableDisables = null,
    int? EnemyKitImmunityPiercingDisables = null,
    // Hidden: the enemies' ready spells (real cooldowns and mana) whose range plus area reaches the hero now, the
    // disables among them, and their damage.
    int? HiddenEnemyReadySpellsReachingMe = null,
    int? HiddenEnemyReadyDisablesReachingMe = null,
    int? HiddenEnemyReadyDamageReachingMe = null)
{
    public static SpellPower Unknown { get; } = new(null, null, null, null, null, null, null, null);
}

// The hero's own tools at this second, from the inventory read every 2 s and the cooldowns that started up to then
// (the player sees all of this on screen). Null when the document has no inventories/cooldowns.
public sealed record Readiness(
    // A teleport (scroll in the teleport slot, or boots of travel) with no cooldown running.
    bool? TeleportReady,
    // An escape or save item (HeroSituationBuilder.EscapeItems) in the inventory, and one of them with no cooldown.
    bool? HasEscapeItem,
    bool? EscapeItemReady,
    // The ultimate, taken as the ability with the longest cooldown seen up to this second (a heuristic: the replay data
    // does not say which one is the ultimate); null until the hero has used it once.
    bool? UltimateReady,
    // How many of the hero's abilities had a cooldown running.
    int? AbilitiesOnCooldown)
{
    public static Readiness Unknown { get; } = new(null, null, null, null, null);
}

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
