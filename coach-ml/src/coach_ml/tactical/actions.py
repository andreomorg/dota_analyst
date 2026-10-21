"""Opt-in consumption of C# timed-use receipts; never derives resource readiness."""
import copy
import hashlib
import math
import re

from ..core.json import parse_json
from ..core.validation import require, AuditError

FACT_ID = 'recorded-actions'
MASKED_ROLE = {'position': None, 'source': 'retrospective-inference-withheld', 'isInferred': True}
LIMITATIONS = [
    'Recorded use is not current inventory, cooldown, mana, target, success or readiness.',
    'Technical roster identity is not player visibility; use order is not causality.',
    'Inferred role is withheld because its observation time is not established.']


def validate_evidence(evidence, hero, anchor):
    require(type(evidence) is dict and set(evidence) == {
        'schemaVersion','heroId','anchorSeconds','windowStartSeconds','heroes','itemUses','abilityUses'},
        'Unexpected action evidence fields')
    require(type(evidence['schemaVersion']) is int and evidence['schemaVersion'] == 1,
            'Unsupported action evidence version')
    require(type(hero) is int and hero > 0 and type(anchor) is int and anchor >= 0,
            'Invalid action anchor identity')
    require(type(evidence['heroId']) is int and evidence['heroId'] == hero
            and type(evidence['anchorSeconds']) is int and evidence['anchorSeconds'] == anchor
            and type(evidence['windowStartSeconds']) is int and evidence['windowStartSeconds'] == anchor-30,
            'Action evidence anchor mismatch')
    roster = evidence['heroes']
    require(type(roster) is list and 1 <= len(roster) <= 10, 'Invalid action roster')
    ids = set()
    for row in roster:
        require(type(row) is dict and set(row) == {'heroId','team','heroName'}, 'Unexpected roster fields')
        require(type(row['heroId']) is int and row['heroId'] > 0 and row['heroId'] not in ids,
                'Invalid or repeated roster hero')
        require(type(row['team']) is int and row['team'] in (0,1), 'Invalid roster team')
        require(type(row['heroName']) is str and len(row['heroName']) <= 128
                and re.fullmatch(r'npc_dota_hero_[a-z0-9_]+',row['heroName']), 'Invalid technical hero name')
        ids.add(row['heroId'])
    require(hero in ids, 'Focal hero missing from roster')
    require([h['heroId'] for h in roster] == sorted(ids), 'Noncanonical roster order')
    for field in ('itemUses','abilityUses'):
        events = evidence[field]
        require(events is None or type(events) is list and len(events) <= 4096, 'Invalid action uses')
        previous = -math.inf
        for event in events or []:
            require(type(event) is dict and set(event) == {'heroId','seconds','name'}, 'Unexpected use fields')
            require(type(event['heroId']) is int and event['heroId'] == hero, 'Use actor mismatch')
            seconds = event['seconds']
            require(type(seconds) in (int,float) and math.isfinite(seconds)
                    and anchor-30 <= seconds <= anchor and previous <= seconds, 'Future, stale or unordered use')
            previous = seconds
            require(type(event['name']) is str and len(event['name']) <= 128
                    and re.fullmatch(r'[a-z0-9_]+',event['name']), 'Invalid technical use key')
    return evidence


def add_actions(request, evidence):
    validate_evidence(evidence,request['heroId'],request['anchorSeconds'])
    require(not any(f['id'] == FACT_ID for f in request['facts']), 'Repeated action fact')
    teams = {h['heroId']:h['team'] for h in evidence['heroes']}
    positions = next(f['value']['samples'] for f in request['facts'] if f['id'] == 'positions')
    require(all(p['heroId'] in teams and teams[p['heroId']] == p['team'] for p in positions),
            'Roster and position identities disagree')
    request['facts'].append({'id':FACT_ID,'kind':'recorded-use-window','sampleSeconds':request['anchorSeconds'],
        'value':copy.deepcopy(evidence),'source':'csharp:CoachingActionEvidenceBuilder',
        'perspective':'recorded','limitations':list(LIMITATIONS)})
    if request['role']['isInferred']:
        request['role'] = dict(MASKED_ROLE)
    return request


def load_action_receipt(directory, source, index):
    """Bind a complete receipt to the selected index and archived source partition.

    Hashes detect drift, not a malicious author. Independent replay comparison remains
    separate evidence; its summary is not treated as cryptographic attestation.
    """
    def sha(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()
    audit = parse_json((directory/'export-audit.json').read_text(encoding='utf-8-sig'))
    require(type(audit.get('schemaVersion')) is int and audit['schemaVersion'] == 1
            and type(audit.get('windowSeconds')) is int and audit['windowSeconds'] == 30
            and type(audit.get('rows')) is int and audit['rows'] > 0
            and type(audit.get('matches')) is int and audit['matches'] > 0
            and audit.get('developmentOnly') is True and audit.get('personalOrFinalFieldsExported') is False,
            'Unsupported action export receipt')
    require(audit['sourceManifestSha256'] == sha(source/'manifest.json'), 'Action source manifest changed')
    require(any(row['name'] == index.name and row['sha256'] == sha(index) for row in audit['sourceIndexes']),
            'Action receipt does not cover this index')
    data = directory/'actions.jsonl'
    require(audit['dataFileSha256'] == sha(data), 'Action data hash changed')
    rows, games = {}, set()
    for line in data.read_text(encoding='utf-8').splitlines():
        row = parse_json(line)
        require(type(row) is dict and set(row) == {'caseId','matchId','sourceMatchHash','parserSchemaVersion','evidence'},
                'Unexpected action receipt fields')
        require(type(row['caseId']) is str and row['caseId'] and row['caseId'] not in rows,
                'Repeated or invalid action case')
        evidence = row['evidence']
        validate_evidence(evidence,evidence['heroId'],evidence['anchorSeconds'])
        require(type(row['matchId']) is str and re.fullmatch('[0-9]+',row['matchId'])
                and type(row['sourceMatchHash']) is str and re.fullmatch('[0-9a-f]{64}',row['sourceMatchHash'])
                and type(row['parserSchemaVersion']) is int and row['parserSchemaVersion'] > 0,
                'Invalid action source identity')
        rows[row['caseId']] = row
        games.add(row['matchId'])
    require(len(rows) == audit['rows'] and len(games) == audit['matches'], 'Action receipt count mismatch')
    return rows, sha(data)


def attach_actions(cases, directory, source, index):
    try:
        rows, sha = load_action_receipt(directory,source,index)
        output = []
        for record, request in cases:
            row = rows.get(record['caseId'])
            require(row is not None and row['matchId'] == record['matchId']
                    and row['sourceMatchHash'] == record['sourceMatchHash'], 'Action case source mismatch')
            enriched = add_actions(copy.deepcopy(request),row['evidence'])
            output.append(({**record,'actionSourceSha256':sha},enriched))
        return output
    except (KeyError,TypeError,AttributeError) as error:
        raise AuditError('Malformed action receipt') from error


def action_review_schema(request):
    from .structured import candidate_schema
    schema = candidate_schema(request)
    schema['properties']['disposition'] = {'type':'string','enum':['conditional-review','insufficient']}
    schema['properties']['preference'] = {'type':'string','enum':['undetermined']}
    return schema
