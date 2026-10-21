"""Project only anchor facts; no episode identifiers or retrospective outcomes in prompts."""
import copy

from ..core.validation import require

UNKNOWN = ['visibility', 'health-mana', 'cooldowns', 'inventory-readiness', 'intent', 'safe-path', 'creep-waves']


def project_scene(context, scene):
    source = scene['atOrBeforeAnchor']
    anchor = source['anchorSeconds']
    require(type(anchor) is int and anchor >= 0, 'Invalid anchor')
    hero = context['heroId']
    require(type(hero) is int and hero > 0, 'Invalid hero')
    role = context['role']
    require(type(role['position']) is int and 1 <= role['position'] <= 5, 'Invalid role')
    samples = [{key: sample[key] for key in ('heroId', 'team', 'sampleSeconds', 'x', 'y')}
               for sample in source['positions']]
    require(type(samples) is list and 0 <= len(samples) <= 10, 'Invalid positions')
    ids = set()
    for sample in samples:
        require(type(sample['heroId']) is int and sample['heroId'] > 0 and sample['heroId'] not in ids,
                'Duplicate or invalid position hero')
        ids.add(sample['heroId'])
        require(type(sample['sampleSeconds']) is int and max(0, anchor-2) <= sample['sampleSeconds'] <= anchor,
                'Future or stale position')
        require(type(sample['team']) is int and sample['team'] in (0, 1), 'Invalid team')
        require(all(type(sample[k]) is int for k in ('x', 'y')), 'Invalid map coordinates')
    missing = source['missingHeroPositions']
    require(type(missing) is int and missing >= 0 and len(samples) + missing == 10, 'Invalid position total')
    viewer = next((s for s in samples if s['heroId'] == hero), None)
    coverage = {'samples': copy.deepcopy(samples), 'missingHeroPositions': missing,
                'viewerObserved': viewer is not None,
                'observedAllies': sum(s['team'] == viewer['team'] and s['heroId'] != hero for s in samples) if viewer else None,
                'unit': 'dota-world-coordinate'}
    facts = [{'id': 'positions', 'kind': 'omniscient-position-coverage', 'sampleSeconds': anchor,
              'value': coverage, 'source': 'decision-document:positions', 'perspective': 'omniscient',
              'limitations': ['Each sample retains its own timestamp; not player visibility.']}]

    def add(identifier, kind, value, seconds, limitation):
        require(type(seconds) is int and 0 <= seconds <= anchor, 'Future fact')
        facts.append({'id': identifier, 'kind': kind, 'sampleSeconds': seconds,
                      'value': copy.deepcopy(value), 'source': 'decision-document:'+identifier,
                      'perspective': 'recorded', 'limitations': [limitation]})

    economy = source.get('economy')
    if economy is not None:
        economy = {key: economy[key] for key in ('sampleSeconds', 'netWorth', 'lastHits')}
        require(anchor-60 <= economy['sampleSeconds'], 'Stale economy')
        require(type(economy['netWorth']) is int and economy['netWorth'] >= 0, 'Invalid net worth')
        require(economy.get('lastHits') is None or type(economy['lastHits']) is int and economy['lastHits'] >= 0,
                'Invalid last hits')
        add('economy', 'earlier-snapshot', economy, economy['sampleSeconds'], 'Net worth is not carried gold.')
    for key, identifier, window, columns, limitation in (
        ('recentPurchases', 'purchases', 90, ('heroId', 'seconds', 'itemKey'), 'Purchase is not delivery, inventory or readiness.'),
        ('recentAbilityUpgrades', 'upgrades', 90, ('heroId', 'seconds', 'abilityName', 'level'), 'Upgrade is not mana or cooldown.'),
        ('recentObjectives', 'objectives', 60,
         ('kind', 'seconds', 'takenBy', 'killerHeroId', 'owner', 'towerTier', 'lane', 'meleeBarracks', 'denied', 'aegisHeroId'),
         'Event is not objective health, pressure or causal value.')):
        events = source.get(key)
        require(events is None or type(events) is list, 'Invalid events')
        if events is not None:
            require(len(events) <= 512, 'Oversized event window; explicit projection review required')
            events = [{column: event[column] for column in columns} for event in events]
            for event in events:
                lower = max(0, anchor-window) if identifier == 'objectives' else anchor-window
                require(type(event['seconds']) is int and lower <= event['seconds'] <= anchor,
                        'Future or stale event')
            latest = max((event['seconds'] for event in events), default=None)
            value = {'status': 'recorded' if events else 'none-recorded', 'count': len(events),
                     'events': events, 'latestEvents': [e for e in events if e['seconds'] == latest]}
        else:
            value = {'status': 'unavailable', 'events': None, 'count': None, 'latestEvents': None}
        add(identifier, 'record-window', value, anchor, limitation)
    unknowns = list(UNKNOWN)
    patch = context['patch']
    if not patch.get('matchPatch') or not patch.get('mechanicsVerified'):
        unknowns.append('patch-mechanics')
    if economy is None:
        unknowns.append('economy')
    if viewer is None:
        unknowns.append('viewer-position')
    # Explicit allow-list: future fields and descriptions cannot leak through dict expansion.
    request = {'schemaVersion': 1, 'task': 'tactical-review-v1', 'heroId': hero,
               'role': {key: copy.deepcopy(role[key]) for key in ('position', 'source', 'isInferred')},
               'anchorSeconds': anchor,
               'patch': {key: copy.deepcopy(patch[key]) for key in ('matchPatch', 'source', 'trainingPatch', 'mechanicsVerified')},
               'facts': facts, 'unknowns': unknowns}
    return request
