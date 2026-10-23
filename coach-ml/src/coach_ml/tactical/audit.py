"""Experimental SFT integrity checks, explicitly not certification of tactical prose."""
from collections import Counter
from itertools import zip_longest
import math
import re

from ..core.json import canonical, digest, parse_json
from ..core.streams import iter_jsonl, iter_json_array
from ..core.validation import require, AuditError
from .contracts import validate_candidate
from .projection import project_scene
from .facts import audit_candidate_facts


def validate_request(request, profile=None):
    try:
        if profile is not None:
            require(request.get('task') == profile, 'Tactical request/profile mismatch')
        if request.get('task') == 'tactical-review-v5':
            return _validate_v5(request)
        return _validate_request(request)
    except (KeyError, TypeError, AttributeError, IndexError) as error:
        raise AuditError('Malformed tactical request') from error


def _validate_v5(request):
    from .situated import FIELDS, _reject_hidden, project_situated_scene
    _reject_hidden(request)
    facts = request['facts']
    by_id = {f['id']: f for f in facts}
    require(len(by_id) == len(facts) and {'situation', 'roster'} <= set(by_id),
            'Missing or duplicate v5 facts')
    legacy = {**request, 'schemaVersion': 1, 'task': 'tactical-review-v1',
              'facts': [f for f in facts if f['id'] not in ('situation', 'roster')]}
    coverage = by_id['positions']['value']
    source = {'anchorSeconds': request['anchorSeconds'], 'positions': coverage['samples'],
              'missingHeroPositions': coverage['missingHeroPositions'],
              'economy': by_id['economy']['value'] if 'economy' in by_id else None}
    for field, name in (('recentPurchases', 'purchases'), ('recentAbilityUpgrades', 'upgrades'),
                        ('recentObjectives', 'objectives')):
        source[field] = by_id[name]['value']['events']
    base = project_scene(legacy, {'atOrBeforeAnchor': source})
    legacy['unknowns'] = base['unknowns']
    _validate_request(legacy)
    value = by_id['situation']['value']
    situation = None if value is None else {
        field: value[group][field] for field, (group, _, _) in FIELDS.items()}
    roster = by_id['roster']['value']
    require(all(h['team'] in ('aliado', 'inimigo') for h in roster), 'Invalid roster relation')
    heroes = [{'heroId': h['heroId'], 'name': h['name'], 'roles': h['roles'],
               'team': 'radiant' if h['team'] == 'aliado' else 'dire'} for h in roster]
    expected = project_situated_scene({
        'schemaVersion': 5, 'decision': {**legacy, 'scenes': [{'atOrBeforeAnchor': source}]},
        'heroes': heroes, 'situations': [{'anchorSeconds': request['anchorSeconds'],
                                          'situation': situation}]}, 0)
    require(canonical(expected) == canonical(request), 'Noncanonical situated request')
    return request


def _validate_request(request):
    require(type(request) is dict and request.get('task') == 'tactical-review-v1', 'Invalid tactical task')
    require(type(request.get('role')) is dict and type(request['role'].get('isInferred')) is bool
            and type(request['role'].get('source')) is str and 0 < len(request['role']['source']) <= 200, 'Invalid tactical role provenance')
    require(type(request.get('patch')) is dict and type(request['patch'].get('mechanicsVerified')) is bool,
            'Invalid patch provenance')
    facts = request.get('facts')
    require(type(facts) is list and 4 <= len(facts) <= 7, 'Invalid tactical facts')
    require(all(type(f) is dict and type(f.get('id')) is str for f in facts), 'Invalid tactical fact ID')
    by_id = {f['id']:f for f in facts}
    require(len(by_id) == len(facts), 'Repeated tactical fact ID')
    require(set(by_id) <= {'positions','economy','purchases','upgrades','objectives','support-transition','recorded-actions'}, 'Unknown tactical fact kind')
    require({'positions','purchases','upgrades','objectives'} <= set(by_id), 'Missing canonical tactical facts')
    coverage = by_id['positions']['value']
    source = {'anchorSeconds':request['anchorSeconds'], 'positions':coverage['samples'],
              'missingHeroPositions':coverage['missingHeroPositions'],
              'economy':by_id['economy']['value'] if 'economy' in by_id else None}
    for field, name in (('recentPurchases','purchases'),('recentAbilityUpgrades','upgrades'),('recentObjectives','objectives')):
        source[field] = by_id[name]['value']['events']
    projection_context = request
    if 'recorded-actions' in by_id and request['role']['isInferred']:
        from .actions import MASKED_ROLE
        require(request['role'] == MASKED_ROLE, 'Retrospective inferred role must be withheld')
        projection_context = {**request,'role':{'position':1,'source':'withheld-placeholder','isInferred':True}}
    expected = project_scene(projection_context, {'atOrBeforeAnchor':source})
    if 'recorded-actions' in by_id:
        from .actions import add_actions
        add_actions(expected,by_id['recorded-actions']['value'])
    without_support = {**request, 'facts':[f for f in facts if f['id'] != 'support-transition']}
    require(canonical(without_support) == canonical(expected), 'Tactical request is not a canonical anchor projection')
    if 'support-transition' in by_id:
        fact = by_id['support-transition']
        require(set(fact) == {'id','kind','sampleSeconds','value','source','perspective','limitations'}, 'Unexpected support fact field')
        require(fact['sampleSeconds'] == request['anchorSeconds'] and fact['kind'] == 'recorded-support-transition'
                and fact['source'] == 'csharp:CoachingSupportTransitionBuilder' and fact['perspective'] == 'omniscient', 'Invalid support provenance')
        value = fact['value']
        require(set(value) == {'allyHeroId','anchorSeconds','unavailableReason','expectedPairedSeconds','observedPairedSeconds',
                               'unit','distanceDecimalPlaces','earlier','atAnchor','change'}, 'Unexpected support value field')
        require(fact['limitations'] == ['Endpoints are not a continuous path; paired seconds are coverage, not movement duration.',
                                        'Nearest observed ally at anchor is not proof of available intervention.'], 'Unrecognized support limitation')
        require(value['allyHeroId'] is None or type(value['allyHeroId']) is int and value['allyHeroId'] > 0, 'Invalid support hero')
        require(value['unavailableReason'] in (None,'insufficient-earlier-window','anchor-ally-unobserved','anchor-viewer-unobserved',
                                                'earlier-both-unobserved','earlier-viewer-unobserved','earlier-ally-unobserved'), 'Invalid support unavailability reason')
        anchor = request['anchorSeconds']
        require(value['anchorSeconds'] == anchor and value['unit'] == 'dota-world-distance'
                and value['distanceDecimalPlaces'] == 2, 'Invalid support units')
        expected_count = 11 if anchor >= 10 else 0
        require(type(value['expectedPairedSeconds']) is int and value['expectedPairedSeconds'] == expected_count
                and type(value['observedPairedSeconds']) is int and 0 <= value['observedPairedSeconds'] <= expected_count,
                'Invalid support coverage')
        require(value['change'] in ('unavailable','recorded-separation-increased','recorded-separation-decreased','recorded-separation-unchanged'),
                'Unknown support change')
        for field, target in (('earlier',anchor-10),('atAnchor',anchor)):
            sample = value[field]
            if sample is None:
                continue
            require(set(sample) == {'targetSeconds','viewerSampleSeconds','allySampleSeconds','distance'}, 'Unexpected support endpoint field')
            require(target >= 0 and type(sample['targetSeconds']) is int and sample['targetSeconds'] == target, 'Invalid support chronology')
            require(all(type(sample[k]) is int and max(0,target-2) <= sample[k] <= target
                        for k in ('viewerSampleSeconds','allySampleSeconds')), 'Future or stale support sample')
            number = sample['distance']
            require(type(number) in (int,float) and math.isfinite(number) and number >= 0 and round(number,2) == number,
                    'Invalid canonical support distance')
        missing = value['earlier'] is None or value['atAnchor'] is None
        require((value['change'] == 'unavailable') == missing, 'Support availability contradiction')
    return request


def audit_sft(dataset, metadata, manifest, partition, profile='tactical-review-v1'):
    from ..audit import load_manifest
    from .pipeline import SYSTEM
    from .prompt_v5 import SYSTEM_V5
    require(profile in ('tactical-review-v1', 'tactical-review-v5'), 'Invalid tactical profile')
    system = SYSTEM_V5 if profile == 'tactical-review-v5' else SYSTEM
    allowed = {'train':{'train','train-known'}, 'validation':{'validation-new','validation-known'}}
    require(partition in allowed, 'Tactical training audit excludes reserved tests and validations')
    matches = load_manifest(manifest)
    inputs, targets, exercises, roles, dispositions = Counter(), Counter(), Counter(), Counter(), Counter()
    used, identities = set(), set()
    sentinel, rows = object(), 0
    for index, (record, meta) in enumerate(zip_longest(iter_jsonl(dataset), iter_json_array(metadata), fillvalue=sentinel)):
        require(record is not sentinel and meta is not sentinel, 'Dataset/metadata length mismatch')
        require(type(meta) is dict and type(meta.get('row')) is int and meta['row'] == index, 'Invalid tactical metadata row')
        match = matches.get(meta.get('matchId'))
        require(match is not None and match['split'] in allowed[partition], 'Tactical partition leakage')
        require(meta.get('sourceHash') == match['sourceHash'], 'Tactical source hash mismatch')
        messages = record.get('messages')
        require(type(messages) is list and len(messages) == 3 and all(type(m) is dict for m in messages)
                and [m.get('role') for m in messages] == ['system','user','assistant'], 'Invalid tactical messages')
        require(all(type(m.get('content')) is str and m['content'].strip() for m in messages), 'Invalid tactical message content')
        require(messages[0]['content'] == system, 'Tactical system instructions changed')
        request, target = parse_json(messages[1]['content']), parse_json(messages[2]['content'])
        require(canonical(request) == canonical(meta.get('request')) and canonical(target) == canonical(meta.get('target')),
                'Tactical metadata alignment mismatch')
        validate_request(request, profile)
        validate_candidate(request,target)
        require(not audit_candidate_facts(request,target)['flags'], 'Tactical target needs factual content review')
        require(type(meta.get('heroId')) is int and request['heroId'] == meta['heroId'], 'Tactical hero identity mismatch')
        episode = meta.get('episodeId')
        require(type(episode) is str and bool(episode), 'Missing tactical scene identity')
        scene = f"{meta['matchId']}/{meta['heroId']}/{episode}"
        require(meta.get('sourceSceneId') == scene and meta.get('exampleId') == scene+'/'+profile, 'Invalid tactical example identity')
        require(meta['exampleId'] not in identities, 'Duplicate tactical scene')
        identities.add(meta['exampleId'])
        require(meta.get('humanReviewed') is False and meta.get('tacticalQualityApproved') is False
                and meta.get('provisionalAccepted') is True, 'Missing provisional review provenance')
        if profile == 'tactical-review-v5' and meta.get('labelOrigin') == 'deterministic-policy-v2':
            # Targets from the situated rule policy: the rule must reproduce the very target, no critic involved.
            from .policy_v2 import target_for
            require(meta.get('policyVersion') == 2 and target_for(request) == (meta.get('policyRule'), target),
                    'Deterministic target does not reproduce from its policy')
        else:
            require(meta.get('labelOrigin') == 'model-generated-and-model-critiqued', 'Missing provisional review provenance')
            for name in ('sourcePredictionsSha256','criticIdentitySha256'):
                require(type(meta.get(name)) is str and re.fullmatch('[0-9a-f]{64}',meta[name]), 'Missing tactical review artifact identity')
        inputs[digest(request)] += 1
        targets[digest(target)] += 1
        exercises[digest(target['exercise'])] += 1
        roles[str(request['role']['position'])] += 1
        dispositions[target['disposition']] += 1
        used.add(meta['matchId'])
        rows += 1
    require(rows > 0, 'Empty tactical dataset')
    return {'schemaVersion':1,'kind':'tactical-sft-integrity','profile':profile,'partition':partition,
            'rows':rows,'matches':len(used),'roles':dict(roles),'dispositions':dict(dispositions),
            'uniqueInputs':len(inputs),'duplicateInputOccurrences':rows-len(inputs),
            'uniqueTargets':len(targets),'uniqueExercises':len(exercises),
            'dominantExerciseShare':max(exercises.values())/rows,'structuralIntegrityPassed':True,
            'inputTargetFactsRechecked':False,'sourceFactsRecomputed':False,'trainingEligibilityGranted':False,
            'tacticalQualityApproved':False,'warnings':['NO_HUMAN_TACTICAL_REVIEW','FREE_PROSE_NOT_FACTUALLY_CERTIFIED',
            'SOURCE_HASH_IDENTITY_IS_NOT_RAW_REPLAY_RECOMPUTATION']}
