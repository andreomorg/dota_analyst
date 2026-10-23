"""Export prediction-model samples (death risk, next region) from the C# internal routes into one gzip CSV per
partition (standard library only)."""
import argparse
import csv
import gzip
import json
import os
import sys
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, Request, build_opener

from ..core.json import parse_json
from ..core.validation import AuditError, require
from ..hashing import file_hash

_SITUATION_V1 = ['heroId', 'team', 'seconds', 'role', 'roleInferred', 'x', 'y', 'region', 'onEnemySide',
                 'nearestAllyDistance', 'alliesNear', 'alliesWithinFightRange', 'alliesAlive', 'enemiesAlive', 'level',
                 'netWorth', 'levelLeadOverEnemies', 'teamNetWorthLead', 'teamExperienceLead',
                 'damageFromHeroesLastMinute', 'teamObserversActive', 'ownTowersLost', 'enemyTowersLost',
                 'secondsSinceLastDeath']
_HIDDEN_V1 = ['hiddenNearestEnemyDistance', 'hiddenEnemiesNear', 'hiddenEnemiesWithinFightRange']
# The situation columns of each feature version, in the order of SituationRow in DotaAnalyst.Application. An export (and
# every model trained on it) keeps its version, so older models stay readable on older exports. Version 2 had never
# been exported by 22/10/2026 (only v1 exports and models-v1 exist), so the ultimate-from-catalog (DATA-D03) and the
# readiness with cooldown resets (DATA-D15) meaning changes stay in v2; once a v2 export is saved, changes bump it.
# GOLD5 before the first v2 export: buyback readiness uses replicated cooldown evidence, never an assumed /12 price.
SITUATION_BY_VERSION = {
    1: _SITUATION_V1 + _HIDDEN_V1,
    2: _SITUATION_V1 + ['activityKnown', 'ownSmokeSecondsAgo', 'teleportSecondsAgo', 'escapeItemSecondsAgo',
                        'itemUsesLast30Seconds', 'abilityUsesLast30Seconds',
                        'damageFromHeroesLast6Seconds', 'damageToHeroesLast6Seconds',
                        'teleportReady', 'hasEscapeItem', 'escapeItemReady', 'ultimateReady', 'abilitiesOnCooldown',
                        'ownBuybackSecondsAgo', 'ownGlyphSecondsAgo', 'ownScanSecondsAgo', 'ownPowerRuneSecondsAgo',
                        'secondsSinceRoshanKill', 'hasAegis', 'allyHasAegis', 'enemyHasAegis',
                        'ownTowersLostTop', 'ownTowersLostMiddle', 'ownTowersLostBottom',
                        'enemyTowersLostTop', 'enemyTowersLostMiddle', 'enemyTowersLostBottom',
                        'health', 'healthShare', 'manaShare', 'lowestAllyHealthShare', 'gold', 'stunned', 'silenced',
                        'invisible', 'visibleEnemies', 'visibleEnemiesNear', 'visibleEnemiesWithinFightRange',
                        'nearestVisibleEnemyDistance',
                        'aheadOfOwnWave', 'ownWaveFrontTop', 'ownWaveFrontMiddle', 'ownWaveFrontBottom',
                        'ownWaveCreepsInLane', 'ownWaveCreepsAtFront', 'seenEnemiesInMyLane',
                        'waveClashPoint', 'waveClashPastOwnTower', 'waveClashShortOfEnemyTower',
                        'ownFrontChangeLast10', 'ownFrontChangeLast30',
                        'ownWaveHealthAtFront', 'ownSiegeInLane', 'ownCreepsOffLane', 'enemyCreepsKilledByTeamLast30',
                        'ownCreepsKilledByEnemyHeroesLast30', 'ownCreepsKilledByEnemyTowerLast30',
                        'enemyCreepsKilledByOwnTowerLast30', 'ownWaveStacked', 'lastHitsAvailable',
                        'deniesAvailable', 'ownSuperCreeps', 'creepHitsOnMeLast10',
                        'waveClashTop', 'waveClashMiddle', 'waveClashBottom', 'frontChangeTopLast30',
                        'frontChangeMiddleLast30', 'frontChangeBottomLast30', 'enemyWavesAtOwnTowers',
                        'ownWavesAtEnemyTowers', 'secondsToNextWave',
                        'isNight', 'estimatedArmor', 'magicResistance', 'moveSpeed', 'averageDamage', 'strength',
                        'agility', 'intellect', 'enemiesHittingMe', 'enemiesIHit', 'iStruckFirst',
                        'laneLastHitsLastMinute', 'neutralsLastMinute', 'deniesLastMinute', 'secondsSinceLastCreepKill',
                        'ownCampsStacked', 'ownCampsEmpty',
                        'intentMoves', 'intentAttacks', 'intentCasts', 'intentDestinationDistance',
                        'intentDestinationAdvance', 'intentDestinationOnEnemySide',
                        'missingEnemies', 'missingEnemyLastSeenDistance', 'missingEnemyLastSeenSecondsAgo',
                        'activeDisables', 'activeSlows', 'activeOtherDebuffs', 'disabledSecondsLast10',
                        'enemiesAfflictingLast10', 'teleporting', 'magicImmune', 'savedNow', 'activeHealBuffs',
                        'activeSpeedBuffs', 'alliesBuffingLast10', 'nearestOwnTowerDistance',
                        'nearestOwnTowerHealthShare', 'nearestEnemyTowerDistance', 'nearestEnemyTowerHealthShare',
                        'ownBuildingsUnderAttack', 'mostKillsOnMeByOneEnemy', 'nearestVisibleEnemyKillsOnMe',
                        'nearestVisibleEnemyKillsOnTeam', 'nearestVisibleEnemyLevelLead',
                        'nearestVisibleEnemyDisablesOnTeam', 'enemyDisablersAlive', 'enemyInitiatorsAlive',
                        'enemyNukersAlive', 'enemyEscapesAlive', 'enemyMeleeAlive', 'iAmMelee', 'laneOpponentMelee',
                        'laneOpponentLevelLead', 'laneOpponentKillsOnMe', 'myKillsOnLaneOpponent',
                        'laneOpponentSeenDistance', 'damageToLaneOpponentLastMinute',
                        'damageFromLaneOpponentLastMinute', 'roleConfidence', 'moveSpeedNow', 'advanceSpeed',
                        'buybackReady', 'underSmoke', 'alliesUnderSmoke', 'inOwnObserverVision', 'alliesWithTeleportReady',
                        'facingEnemyBase', 'facingNearestSeenEnemy', 'stashItems', 'ownCouriersAlive',
                        'enemyCouriersAlive', 'physicalTakenLast6', 'magicalTakenLast6', 'pureTakenLast6',
                        'healingReceivedLast6', 'healingDoneLast6',
                        'goldFromCreepsLastMinute', 'goldFromNeutralsLastMinute', 'goldFromHeroesLastMinute',
                        'goldFromObjectivesLastMinute', 'experienceLastMinute', 'allyPingsNearLast10', 'ownPingsLast10',
                        'enemySpellsOnMeLast10', 'mySpellsOnEnemiesLast10', 'secondsToNextPowerRune',
                        'killStreak', 'recentDeaths', 'teamKillsLastMinute', 'enemyKillsLastMinute',
                        'enemiesWithUltimateLevel', 'secondsSinceMyBigItem', 'enemiesWithStunInKit',
                        'enemiesWithSilenceOrHexInKit', 'enemiesWithEscapeInKit', 'enemiesWithInvisibilityInKit',
                        'myDisableReady', 'myEscapeReady',
                        'observerCoverageOwnJungle', 'observerCoverageEnemyJungle', 'observerCoverageRiver',
                        'myReadySpellDamage', 'myReadySpellHealing', 'myReadySpellsManaCost', 'myManaAfterReadySpells',
                        'alliesReadySpellHealingNear', 'enemyKitDamageAlive', 'myReadyAreaSpells',
                        'myReadySingleTargetSpells', 'myReadySpellMaxRange', 'myReadyDisablesReachingSeenEnemy',
                        'enemyKitDisableMaxRange', 'enemyKitUndispellableDisables', 'enemyKitImmunityPiercingDisables',
                        'neutralTierOpen', 'secondsToNextNeutralTier', 'myNeutralArtifactTier', 'myNeutralTiersBehind',
                        'myNeutralActiveReady', 'myNeutralEnhancementLevel', 'myMadstones', 'canCraftNeutralNow',
                        'secondsSinceNeutralCraft']
       + _HIDDEN_V1 + ['hiddenEnemySmokeSecondsAgo', 'hiddenEnemiesHealthShare', 'hiddenEnemyScanSecondsAgo',
                       'hiddenEnemyPowerRuneSecondsAgo', 'hiddenEnemyWaveAhead', 'hiddenEnemyWaveCreepsAtFront',
                       'hiddenWaveHealthBalanceAtFront', 'hiddenEnemyWaveStacked', 'hiddenEnemyCampsStacked',
                       'hiddenEnemyBuildingsUnderAttack', 'hiddenEnemiesInMyLane', 'hiddenInEnemyObserverVision',
                       'hiddenEnemyObserverCoverageOwnJungle',
                       'hiddenEnemiesUnderSmoke', 'hiddenEnemiesWithBuyback', 'hiddenEnemiesWithTeleportReady',
                       'hiddenPowerRuneOnMap', 'hiddenEnemyBigItemsLast2Minutes', 'hiddenEnemiesWithDisableReadyNear',
                       'hiddenEnemiesUltimateReadyNear',
                       'hiddenEnemiesBlackKingBarReadyNear', 'hiddenLaneOpponentDistance',
                       'hiddenLaneOpponentNetWorthLead', 'hiddenSeenByEnemies',
                       'hiddenEnemiesReadySpellDamageNear', 'hiddenEnemiesReadySpellHealingNear',
                       'hiddenEnemyReadySpellsReachingMe', 'hiddenEnemyReadyDisablesReachingMe',
                       'hiddenEnemyReadyDamageReachingMe', 'hiddenEnemyNeutralActivesReadyNear', 'hiddenEnemyMadstones',
                       'hiddenEnemyNeutralTiersBehind'],
}
# Each model's label columns, after the situation; and the API route of its samples.
LABELS = {'death-risk': ['diesSoon', 'diedAtSeconds'], 'next-region': ['regionLater']}
ROUTES = {'death-risk': 'death-risk', 'next-region': 'next-region'}
FEATURE_VERSION = max(SITUATION_BY_VERSION)


def columns_of(version, kind='death-risk'):
    """The CSV columns (after matchId) of an export of this version and kind. Version 1 only had death risk."""
    require(version in SITUATION_BY_VERSION and kind in LABELS and (version > 1 or kind == 'death-risk'),
            f'No export of version {version} and kind {kind}')
    return SITUATION_BY_VERSION[version] + LABELS[kind]


# What the API sends today for death risk (the default kind).
COLUMNS = columns_of(FEATURE_VERSION)
# A sanity cap: ~8 KB per sample with the v2 columns, ~3,600 samples in a 60-minute match (~30 MB).
MAX_BODY = 128 * 1024 * 1024
MAX_RETRY_AFTER = 60


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class HttpFetcher:
    """GET one match; returns (status, parsed body or None, Retry-After seconds or None)."""

    def __init__(self, base_url, key, kind='death-risk', timeout=60, path=None):
        require(base_url.startswith(('http://', 'https://')) and base_url.endswith('/'),
                'The API URL must be http(s) and end with /')
        require(len(key) >= 16, 'The machine key is missing or too short')
        require(path is not None or kind in ROUTES, f'Unknown kind {kind}')
        # path: another internal route under api/internal/ (e.g. position-estimates), the match ID appended.
        self.base_url, self.key, self.timeout = base_url, key, timeout
        self.path = path or f'{ROUTES[kind]}/samples'
        self.opener = build_opener(NoRedirect())

    def __call__(self, match_id):
        url = f'{self.base_url}api/internal/{self.path}/{match_id}'
        request = Request(url, headers={'Accept': 'application/json', 'Authorization': 'Bearer ' + self.key})
        try:
            with self.opener.open(request, timeout=self.timeout) as response:
                raw = response.read(MAX_BODY + 1)
            require(len(raw) <= MAX_BODY, 'Response exceeds limit')
            return 200, parse_json(raw.decode('utf-8')), None
        except HTTPError as exc:
            retry = exc.headers.get('Retry-After') if exc.headers else None
            exc.close()
            return exc.code, None, int(retry) if retry and retry.isdigit() else None
        except (TimeoutError, URLError, OSError) as exc:
            raise AuditError(f'API unreachable: {exc}') from exc


def read_manifest(path):
    manifest = parse_json(Path(path).read_text(encoding='utf-8-sig'))
    matches = manifest.get('matches')
    require(isinstance(matches, list) and matches, 'Manifest without matches')
    seen = set()
    for match in matches:
        require(isinstance(match.get('matchId'), str) and match['matchId'].isdigit(), 'Invalid matchId in manifest')
        require(isinstance(match.get('split'), str) and match['split'], 'Match without split in manifest')
        require(match['matchId'] not in seen, f'Repeated match {match["matchId"]}')
        seen.add(match['matchId'])
    return matches


def validate_set(body, match_id, kind='death-risk'):
    """The rows of a sample set, flat (situation columns, then the labels); refuses any other set of fields."""
    require(isinstance(body, dict), 'Sample set is not an object')
    require(body.get('featureVersion') == FEATURE_VERSION, f'Unexpected featureVersion for {match_id}')
    require(body.get('matchId') == match_id, f'Sample set of another match for {match_id}')
    samples = body.get('samples')
    require(isinstance(samples, list), f'Samples missing for {match_id}')
    situation, labels = SITUATION_BY_VERSION[FEATURE_VERSION], LABELS[kind]
    rows = []
    for sample in samples:
        require(isinstance(sample, dict) and list(sample) == ['situation', *labels], f'Unexpected fields for {match_id}')
        require(isinstance(sample['situation'], dict) and list(sample['situation']) == situation,
                f'Unexpected situation columns for {match_id}')
        if kind == 'death-risk':
            require(isinstance(sample['diesSoon'], bool), f'Invalid label for {match_id}')
        else:
            require(sample['regionLater'] is None or isinstance(sample['regionLater'], str), f'Invalid label for {match_id}')
        rows.append({**sample['situation'], **{label: sample[label] for label in labels}})
    return rows


def label_stats(kind, rows):
    """What the summary counts per partition: deaths for death risk, each destination for next region."""
    if kind == 'death-risk':
        return {'deaths': sum(row['diesSoon'] for row in rows)}
    counts = {}
    for row in rows:
        destination = row['regionLater'] or 'dead'
        counts[destination] = counts.get(destination, 0) + 1
    return counts


def cell(value):
    if value is None:
        return ''
    if isinstance(value, bool):
        return '1' if value else '0'
    return str(value)


def export(matches, fetch, output, partitions=None, limit=None, interval=1.05, sleep=time.sleep, log=print,
           kind='death-risk'):
    """Fetch every match of the chosen partitions and write <partition>.csv.gz plus export-manifest.json (last)."""
    require(kind in LABELS, f'Unknown kind {kind}')
    columns = columns_of(FEATURE_VERSION, kind)
    output = Path(output)
    require(not output.exists(), f'Output already exists: {output}')
    # Reserved partitions are never exported: left out by default, refused when asked for.
    require(not any(split.startswith('reserved-') for split in partitions or []), 'Reserved partitions are never exported')
    chosen = [match for match in matches if not match['split'].startswith('reserved-')
              and (partitions is None or match['split'] in partitions)]
    chosen = chosen[:limit] if limit else chosen
    require(chosen, 'No matches to export')
    output.mkdir(parents=True)

    writers, files, stats = {}, {}, {}
    skipped = {'noDocument': [], 'outdated': []}
    try:
        for index, match in enumerate(chosen):
            match_id, split = match['matchId'], match['split']
            body = fetch_with_retry(fetch, match_id, sleep)
            if isinstance(body, str):
                skipped[body].append(match_id)
            else:
                rows = validate_set(body, match_id, kind)
                if split not in writers:
                    files[split] = gzip.open(output / f'{split}.csv.gz', 'xt', encoding='utf-8', newline='')
                    writers[split] = csv.writer(files[split])
                    writers[split].writerow(['matchId', *columns])
                    stats[split] = {'matches': 0, 'rows': 0, 'labels': {}}
                for row in rows:
                    writers[split].writerow([match_id, *(cell(row[column]) for column in columns)])
                stats[split]['matches'] += 1
                stats[split]['rows'] += len(rows)
                for label, count in label_stats(kind, rows).items():
                    stats[split]['labels'][label] = stats[split]['labels'].get(label, 0) + count
            if (index + 1) % 50 == 0:
                log(json.dumps({'exported': index + 1, 'of': len(chosen)}))
            if index + 1 < len(chosen):
                sleep(interval)
    finally:
        for stream in files.values():
            stream.close()

    def partition(split, values):
        entry = {'matches': values['matches'], 'rows': values['rows'], 'file': f'{split}.csv.gz',
                 'sha256': file_hash(output / f'{split}.csv.gz')}
        if kind == 'death-risk':
            # The same fields as version 1 exports, which readers already know.
            deaths = values['labels'].get('deaths', 0)
            return entry | {'deaths': deaths, 'deathRate': round(deaths / values['rows'], 5) if values['rows'] else None}
        return entry | {'labels': dict(sorted(values['labels'].items()))}

    summary = {
        'schemaVersion': 1,
        'featureVersion': FEATURE_VERSION,
        'kind': kind,
        'partitions': {split: partition(split, values) for split, values in sorted(stats.items())},
        'skipped': skipped,
        'humanReviewed': False,
    }
    # Written last: a folder without it is an interrupted export, not a dataset.
    with (output / 'export-manifest.json').open('x', encoding='utf-8') as stream:
        stream.write(json.dumps(summary, indent=2) + '\n')
    return summary


def fetch_with_retry(fetch, match_id, sleep, attempts=5):
    """The parsed sample set, or 'noDocument'/'outdated'. Waits on 429; stops on anything unexpected."""
    for _ in range(attempts):
        status, body, retry_after = fetch(match_id)
        if status == 200:
            return body
        if status == 404:
            return 'noDocument'
        if status == 422:
            return 'outdated'
        if status == 429:
            sleep(min(retry_after or 5, MAX_RETRY_AFTER))
            continue
        require(status != 401, 'The API refused the machine key (401)')
        raise AuditError(f'Unexpected HTTP {status} for match {match_id}')
    raise AuditError(f'Still rate limited after {attempts} attempts for match {match_id}')


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml risk-export')
    parser.add_argument('--manifest', required=True, help='manifest.json with matchId and split per match')
    parser.add_argument('--api-url', required=True, help='Local API base URL ending with /')
    parser.add_argument('--key-env', default='COACH_ML_API_KEY', help='Environment variable holding the machine key')
    parser.add_argument('--output', required=True, help='New folder for the export')
    parser.add_argument('--partition', action='append', help='Only these partitions (repeatable)')
    parser.add_argument('--limit', type=int, help='Only the first N matches (smoke run)')
    parser.add_argument('--kind', choices=sorted(LABELS), default='death-risk', help='Which model the samples are for')
    args = parser.parse_args(argv)
    try:
        fetch = HttpFetcher(args.api_url, os.environ.get(args.key_env, ''), args.kind)
        summary = export(read_manifest(args.manifest), fetch, args.output, args.partition, args.limit, kind=args.kind)
    except (AuditError, OSError) as exc:
        print(json.dumps({'error': str(exc)}), file=sys.stderr)
        return 2
    print(json.dumps({split: {key: value for key, value in values.items() if key not in ('file', 'sha256')}
                      for split, values in summary['partitions'].items()} | {'skipped': {
                          key: len(value) for key, value in summary['skipped'].items()}}))
    return 0
