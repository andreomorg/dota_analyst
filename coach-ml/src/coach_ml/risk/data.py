"""Load a death-risk export into feature matrices (numpy imported lazily by the callers' module)."""
import csv
import gzip
import json
from pathlib import Path

from ..core.json import parse_json
from ..core.validation import require
from ..hashing import file_hash
from .export import SITUATION_BY_VERSION, columns_of

REGIONS = ['ownBase', 'enemyBase', 'topLane', 'middleLane', 'bottomLane', 'river', 'ownJungle', 'enemyJungle']
# The next-region model's classes: a region 30 s later, or dead then.
DESTINATIONS = REGIONS + ['dead']
DEVELOPMENT = ['train', 'train-known']
VALIDATION = ['validation-new']
FINAL_TEST = 'test-new'

# What the player could know at that second (positions of allies, the scoreboard, the clock, the minimap of their team).
_VISIBLE_V1 = ['seconds', 'team', 'role', 'x', 'y', 'region', 'onEnemySide', 'nearestAllyDistance', 'alliesNear',
               'alliesWithinFightRange', 'alliesAlive', 'enemiesAlive', 'level', 'netWorth', 'levelLeadOverEnemies',
               'teamNetWorthLead', 'teamExperienceLead', 'damageFromHeroesLastMinute', 'teamObserversActive',
               'ownTowersLost', 'enemyTowersLost', 'secondsSinceLastDeath']
# The player's own recent activity: unknown (NaN) when the export says the replay had none.
ACTIVITY = ['ownSmokeSecondsAgo', 'teleportSecondsAgo', 'escapeItemSecondsAgo', 'itemUsesLast30Seconds',
            'abilityUsesLast30Seconds', 'damageFromHeroesLast6Seconds', 'damageToHeroesLast6Seconds',
            'ownBuybackSecondsAgo', 'ownGlyphSecondsAgo', 'ownScanSecondsAgo', 'ownPowerRuneSecondsAgo']
# The enemies' activity (hidden): unknown too when the replay had no activity.
HIDDEN_ACTIVITY = ['hiddenEnemySmokeSecondsAgo', 'hiddenEnemyScanSecondsAgo', 'hiddenEnemyPowerRuneSecondsAgo']
# Roshan and the Aegis: from the objectives every analysis has, so never "unknown".
MAP_STATE = ['secondsSinceRoshanKill', 'hasAegis', 'allyHasAegis', 'enemyHasAegis',
             'ownTowersLostTop', 'ownTowersLostMiddle', 'ownTowersLostBottom',
             'enemyTowersLostTop', 'enemyTowersLostMiddle', 'enemyTowersLostBottom']
# The hero's own tools (empty, so NaN, for documents parsed before inventories and cooldowns existed).
READINESS = ['teleportReady', 'hasEscapeItem', 'escapeItemReady', 'ultimateReady', 'abilitiesOnCooldown']
# Health, mana and gold on screen (empty, so NaN, for documents parsed before vitals existed).
CONDITION = ['health', 'healthShare', 'manaShare', 'lowestAllyHealthShare', 'gold', 'stunned', 'silenced', 'invisible',
             # The enemies the team saw: the fog of war, from the game itself.
             'visibleEnemies', 'visibleEnemiesNear', 'visibleEnemiesWithinFightRange', 'nearestVisibleEnemyDistance']
# The hero against its own creep waves (empty without waves or outside a lane).
WAVES = ['aheadOfOwnWave', 'ownWaveFrontTop', 'ownWaveFrontMiddle', 'ownWaveFrontBottom', 'ownWaveCreepsInLane',
         'ownWaveCreepsAtFront', 'seenEnemiesInMyLane',
         # Where the lane is (between both fronts, against the towers) and where it is going.
         'waveClashPoint', 'waveClashPastOwnTower', 'waveClashShortOfEnemyTower', 'ownFrontChangeLast10',
         'ownFrontChangeLast30',
         # The own wave's strength, own creeps pulled off the lanes, and who killed the lane's creeps in 30 s.
         'ownWaveHealthAtFront', 'ownSiegeInLane', 'ownCreepsOffLane', 'enemyCreepsKilledByTeamLast30',
         'ownCreepsKilledByEnemyHeroesLast30', 'ownCreepsKilledByEnemyTowerLast30', 'enemyCreepsKilledByOwnTowerLast30',
         'ownWaveStacked',
         # Last hits / denies on offer, own super creeps, creep aggro on the hero.
         'lastHitsAvailable', 'deniesAvailable', 'ownSuperCreeps', 'creepHitsOnMeLast10',
         # The whole map: all three lanes, waves at towers, and the spawn clock.
         'waveClashTop', 'waveClashMiddle', 'waveClashBottom', 'frontChangeTopLast30', 'frontChangeMiddleLast30',
         'frontChangeBottomLast30', 'enemyWavesAtOwnTowers', 'ownWavesAtEnemyTowers', 'secondsToNextWave']
# Night, the hero's attributes, the fight around it and its recent farm (empty, so NaN, for older documents).
BODY_FIGHT_FARM = ['isNight', 'estimatedArmor', 'magicResistance', 'moveSpeed', 'averageDamage', 'strength', 'agility',
                   'intellect', 'enemiesHittingMe', 'enemiesIHit', 'iStruckFirst', 'laneLastHitsLastMinute',
                   'neutralsLastMinute', 'deniesLastMinute', 'secondsSinceLastCreepKill', 'ownCampsStacked',
                   'ownCampsEmpty']
# The player's own orders (intent). The decision itself: the death-risk model may read it ("heading into the enemy"),
# the next-region model must not (the destination clicked now gives away where the hero is 30 s later).
INTENT = ['intentMoves', 'intentAttacks', 'intentCasts', 'intentDestinationDistance', 'intentDestinationAdvance',
          'intentDestinationOnEnemySide']
# Enemies missing from the team's view (the minimap shows where they were last seen).
MISSING = ['missingEnemies', 'missingEnemyLastSeenDistance', 'missingEnemyLastSeenSecondsAgo']
# Enemy debuffs on the hero (the player sees them on the HUD).
AFFLICTIONS = ['activeDisables', 'activeSlows', 'activeOtherDebuffs', 'disabledSecondsLast10', 'enemiesAfflictingLast10']
# The hero's own and allied buffs (also on the HUD).
BOONS = ['teleporting', 'magicImmune', 'savedNow', 'activeHealBuffs', 'activeSpeedBuffs', 'alliesBuffingLast10']
# The towers around the hero and the team's buildings losing health.
BUILDINGS = ['nearestOwnTowerDistance', 'nearestOwnTowerHealthShare', 'nearestEnemyTowerDistance',
             'nearestEnemyTowerHealthShare', 'ownBuildingsUnderAttack']
# The matchup: what the enemies already did to the hero and its team (the scoreboard, the HUD).
THREAT = ['mostKillsOnMeByOneEnemy', 'nearestVisibleEnemyKillsOnMe', 'nearestVisibleEnemyKillsOnTeam',
          'nearestVisibleEnemyLevelLead', 'nearestVisibleEnemyDisablesOnTeam',
          # The draft (pick screen): living enemies by catalog role, and melee.
          'enemyDisablersAlive', 'enemyInitiatorsAlive', 'enemyNukersAlive', 'enemyEscapesAlive', 'enemyMeleeAlive']
# The direct lane opponent (opposing role).
LANE_OPPONENT = ['iAmMelee', 'laneOpponentMelee', 'laneOpponentLevelLead', 'laneOpponentKillsOnMe',
                 'myKillsOnLaneOpponent', 'laneOpponentSeenDistance', 'damageToLaneOpponentLastMinute',
                 'damageFromLaneOpponentLastMinute',
                 # How sure the estimated position is (empty with a role-queue position).
                 'roleConfidence']
# Movement, buyback, smoke and own ward vision.
SURROUNDINGS = ['moveSpeedNow', 'advanceSpeed', 'buybackReady', 'underSmoke', 'alliesUnderSmoke', 'inOwnObserverVision',
                'alliesWithTeleportReady',
                # Facing, stash, couriers, damage taken by type and healing in 6 s.
                'facingEnemyBase', 'facingNearestSeenEnemy', 'stashItems', 'ownCouriersAlive', 'enemyCouriersAlive',
                'physicalTakenLast6', 'magicalTakenLast6', 'pureTakenLast6', 'healingReceivedLast6', 'healingDoneLast6',
                # Income by source, pings, spells traded, the power rune clock.
                'goldFromCreepsLastMinute', 'goldFromNeutralsLastMinute', 'goldFromHeroesLastMinute',
                'goldFromObjectivesLastMinute', 'experienceLastMinute', 'allyPingsNearLast10', 'ownPingsLast10',
                'enemySpellsOnMeLast10', 'mySpellsOnEnemiesLast10', 'secondsToNextPowerRune',
                # The momentum (scoreboard).
                'killStreak', 'recentDeaths', 'teamKillsLastMinute', 'enemyKillsLastMinute', 'enemiesWithUltimateLevel',
                'secondsSinceMyBigItem',
                # The kits: enemies' disables/escapes from the draft, own disable/escape ready.
                'enemiesWithStunInKit', 'enemiesWithSilenceOrHexInKit', 'enemiesWithEscapeInKit',
                'enemiesWithInvisibilityInKit', 'myDisableReady', 'myEscapeReady',
                # How much of each jungle and the river the team's observers see.
                'observerCoverageOwnJungle', 'observerCoverageEnemyJungle', 'observerCoverageRiver',
                # What the spells can do now: own ready damage/healing/mana, allies' healing near, enemy kit damage.
                'myReadySpellDamage', 'myReadySpellHealing', 'myReadySpellsManaCost', 'myManaAfterReadySpells',
                'alliesReadySpellHealingNear', 'enemyKitDamageAlive',
                # Shape and reach (build of the match): own ready area/single-target spells, range, disables that
                # reach the seen enemy; enemies' learned disables: reach, undispellable, through spell immunity.
                'myReadyAreaSpells', 'myReadySingleTargetSpells', 'myReadySpellMaxRange',
                'myReadyDisablesReachingSeenEnemy', 'enemyKitDisableMaxRange', 'enemyKitUndispellableDisables',
                'enemyKitImmunityPiercingDisables',
                # Neutral items: tier open and the next, own artifact tier/behind/active, enhancement, Madstones, crafts.
                'neutralTierOpen', 'secondsToNextNeutralTier', 'myNeutralArtifactTier', 'myNeutralTiersBehind',
                'myNeutralActiveReady', 'myNeutralEnhancementLevel', 'myMadstones', 'canCraftNeutralNow',
                'secondsSinceNeutralCraft']
# Patch terrain: absent map or tree history remains NaN; vision columns describe known geometric sources.
TERRAIN = ['terrainLevel', 'nearestSeenEnemyTerrainLevel', 'terrainHeightAdvantage', 'onEnemyHighGround',
           'standingTreesWithin300', 'standingTreesWithin600', 'treeLineDistance', 'terrainOpenness', 'inNoWardZone',
           'activeRoshanPitDistance', 'roshanAlive', 'nearMapObjective', 'nearCurrent', 'teamWatchersActive']
MOBILE_VISION = ['teamLocalVisionCoverage', 'teamMobileVisionSourcesNear']
COURIER_FLIGHT = ['ownCourierAliveFlying']
VISIBLE_BY_VERSION = {1: _VISIBLE_V1,
                      2: _VISIBLE_V1 + ACTIVITY + READINESS + MAP_STATE + CONDITION + WAVES + BODY_FIGHT_FARM + INTENT
                      + MISSING + AFFLICTIONS + BOONS + BUILDINGS + THREAT + LANE_OPPONENT + SURROUNDINGS
                      + TERRAIN + MOBILE_VISION + COURIER_FLIGHT}
# Fog of war: only to measure how much the visible part explains, never for tips.
_HIDDEN_V1 = ['hiddenNearestEnemyDistance', 'hiddenEnemiesNear', 'hiddenEnemiesWithinFightRange']
HIDDEN_BY_VERSION = {1: _HIDDEN_V1, 2: _HIDDEN_V1 + ['hiddenEnemySmokeSecondsAgo', 'hiddenEnemiesHealthShare',
                                                     'hiddenEnemyScanSecondsAgo', 'hiddenEnemyPowerRuneSecondsAgo',
                                                     'hiddenEnemyWaveAhead', 'hiddenEnemyWaveCreepsAtFront',
                                                     'hiddenWaveHealthBalanceAtFront', 'hiddenEnemyWaveStacked',
                                                     'hiddenEnemyCampsStacked',
                                                     'hiddenEnemyBuildingsUnderAttack',
                                                     'hiddenEnemiesInMyLane', 'hiddenInEnemyObserverVision',
                                                     'hiddenEnemyObserverCoverageOwnJungle',
                                                     'hiddenEnemiesUnderSmoke', 'hiddenEnemiesWithBuyback',
                                                     'hiddenEnemiesWithTeleportReady', 'hiddenPowerRuneOnMap',
                                                     'hiddenEnemyBigItemsLast2Minutes', 'hiddenEnemiesWithDisableReadyNear',
                                                     'hiddenEnemiesUltimateReadyNear',
                                                     'hiddenEnemiesBlackKingBarReadyNear', 'hiddenLaneOpponentDistance',
                                                     'hiddenLaneOpponentNetWorthLead', 'hiddenSeenByEnemies',
                                                     'hiddenEnemiesReadySpellDamageNear',
                                                     'hiddenEnemiesReadySpellHealingNear',
                                                     'hiddenEnemyReadySpellsReachingMe',
                                                     'hiddenEnemyReadyDisablesReachingMe',
                                                     'hiddenEnemyReadyDamageReachingMe',
                                                     'hiddenEnemyNeutralActivesReadyNear', 'hiddenEnemyMadstones',
                                                     'hiddenEnemyNeutralTiersBehind', 'hiddenInEnemyHeroVision',
                                                     'hiddenEnemyObserversCovering', 'hiddenInEnemyMobileVisionRange',
                                                     'hiddenEnemyCourierAliveFlying', 'hiddenRoshanAlive']}
# The simple model to beat: three numbers anyone would look at first.
SIMPLE = ['nearestAllyDistance', 'onEnemySide', 'seconds']
CATEGORICAL = ['region']
ROLE_INFERRED_UNTIL = 600
BLOCK_ROWS = 100_000


def read_export(folder):
    """The export manifest after checking every file against its hash."""
    folder = Path(folder)
    manifest = parse_json((folder / 'export-manifest.json').read_text(encoding='utf-8'))
    require(manifest.get('featureVersion') in SITUATION_BY_VERSION, 'Export of an unknown feature version')
    # Exports made before there were two models have no kind: they are death risk.
    manifest.setdefault('kind', 'death-risk')
    for split, values in manifest['partitions'].items():
        require(file_hash(folder / values['file']) == values['sha256'], f'{split} changed after the export')
    return manifest


def feature_version(folder):
    return read_export(folder)['featureVersion']


def number(text):
    return float('nan') if text == '' else float(text)


def encode(row, column):
    value = row[column]
    if column == 'team':
        return 0.0 if value == 'radiant' else 1.0
    if column == 'region':
        return float(REGIONS.index(value))
    if (column == 'role' and value != '' and row['roleInferred'] == '1' and 'roleConfidence' not in row
            and int(row['seconds']) < ROLE_INFERRED_UNTIL):
        # Version-1 exports: an inferred role looked at the net worth at 10:00, the future before then. Version 2 has
        # roleConfidence: its inferred role is estimated at that very second (PositionEstimator), with no future.
        return float('nan')
    if (column in ACTIVITY or column in HIDDEN_ACTIVITY) and row.get('activityKnown') == '0':
        # The replay had no activity: unknown, not "nothing was used".
        return float('nan')
    return number(value)


def label_of(row, kind):
    """Death risk: died in the window (0/1). Next region: the DESTINATIONS index of where the hero was later."""
    if kind == 'death-risk':
        return row['diesSoon'] == '1'
    return DESTINATIONS.index(row['regionLater'] or 'dead')


def load(folder, partitions, columns, np, kind='death-risk'):
    """(features, labels, match IDs, extra) for the partitions; extra keeps role, seconds, hero and death second for
    the reports (never model inputs)."""
    manifest = read_export(folder)
    require(manifest['kind'] == kind, f'This export is for {manifest["kind"]}, not {kind}')
    expected = ['matchId', *columns_of(manifest['featureVersion'], kind)]
    require(set(columns) <= set(expected), 'A model column is not in this export version')
    # Rows become float32 blocks as they are read: millions of rows as Python lists would take several GB.
    blocks, pending, labels, groups, roles, seconds, heroes, died_at = [], [], [], [], [], [], [], []
    for split in partitions:
        require(split in manifest['partitions'], f'Partition {split} not in the export')
        with gzip.open(Path(folder) / manifest['partitions'][split]['file'], 'rt', encoding='utf-8', newline='') as stream:
            reader = csv.DictReader(stream)
            require(reader.fieldnames == expected, f'Unexpected columns in {split}')
            for row in reader:
                pending.append([encode(row, column) for column in columns])
                if len(pending) == BLOCK_ROWS:
                    blocks.append(np.asarray(pending, dtype=np.float32))
                    pending = []
                labels.append(label_of(row, kind))
                groups.append(int(row['matchId']))
                roles.append(row['role'])
                seconds.append(int(row['seconds']))
                heroes.append(int(row['heroId']))
                died_at.append(int(row['diedAtSeconds']) if row.get('diedAtSeconds') else -1)
    require(labels, 'No rows to load')
    if pending:
        blocks.append(np.asarray(pending, dtype=np.float32))
    return (np.concatenate(blocks), np.asarray(labels, dtype=np.int8), np.asarray(groups, dtype=np.int64),
            {'role': roles, 'seconds': np.asarray(seconds, dtype=np.int32), 'heroId': np.asarray(heroes, dtype=np.int32),
             # -1 when the hero lived through the window.
             'diedAtSeconds': np.asarray(died_at, dtype=np.int32)})


def export_identity(folder):
    manifest = read_export(folder)
    return {split: values['sha256'] for split, values in sorted(manifest['partitions'].items())}


def dumps(value):
    return json.dumps(value, indent=2, sort_keys=True) + '\n'
