"""Build a new provisional curriculum without touching active training files."""
import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path
import sys

from .audit import AuditError, canonical, digest, iter_jsonl, load_manifest, parse_json, require
from .competencies import COMPETENCIES, expected_response, instructions_for, project_request
from .hashing import file_hash


def state_for(skill, response):
    observation = response['observation']
    if skill == 'position-coverage':
        return observation['sampleSynchronization']
    if skill == 'support-change':
        return observation['change'] + '/' + observation['pairedCoverage']
    if skill == 'economy-snapshot':
        age = observation['sampleAgeSeconds']
        return 'unavailable' if age is None else 'older-than-minute' if age > 60 else 'sampled'
    return observation['status']


def select_balanced(candidates, cap):
    remaining = list(candidates.values())
    counts = {name: Counter() for name in ['role', 'phase', 'state']}
    selected = []
    while remaining and len(selected) < cap:
        chosen = min(remaining, key=lambda c: (counts['state'][c['state']], counts['role'][c['role']],
                                               counts['phase'][c['phase']], c['order']))
        remaining.remove(chosen)
        selected.append(chosen)
        for key in counts:
            counts[key][chosen[key]] += 1
    return selected


def build(source, output, cap=6, policy_version=1):
    source, output = Path(source), Path(output)
    require(type(policy_version) is int and policy_version in [1, 2], 'Unknown review policy version')
    instructions = instructions_for(policy_version)
    require(type(cap) is int and 1 <= cap <= 6, 'Per-match cap must be between one and six')
    manifest = load_manifest(source / 'manifest.json')
    audit = parse_json((source / 'audit.json').read_text(encoding='utf-8-sig'))
    allowed = {'train-known': 'train', 'train': 'train', 'validation-new': 'validation'}
    files = []
    for split in allowed:
        relative = f'model-corpus/{split}.scenes.jsonl'
        file = source / relative
        require(file_hash(file) == audit['corpusFiles'][relative]['sha256'], f'Source corpus changed: {relative}')
        files.append((split, file))
    output.mkdir(parents=True, exist_ok=False)
    writers, counts, games, seen_ids = {}, defaultdict(Counter), defaultdict(set), set()
    role_counts, state_counts, phase_counts = defaultdict(Counter), defaultdict(Counter), defaultdict(Counter)
    example_ids = set()
    sources_read = Counter()
    stem = lambda partition: 'development' if partition == 'train' else 'validation'
    for partition in ['train', 'validation']:
        writers[partition] = ((output / f'{stem(partition)}.jsonl.tmp').open('w', encoding='utf-8'),
                              (output / f'{stem(partition)}-metadata.json.tmp').open('w', encoding='utf-8'))
        writers[partition][1].write('[')

    def flush(match_id, partition, candidates):
        if match_id is None:
            return
        require(match_id not in games[partition], 'Noncontiguous or duplicate source match')
        games[partition].add(match_id)
        for skill in COMPETENCIES:
            for chosen in select_balanced(candidates[skill], cap):
                scene, request, response = chosen['scene'], chosen['request'], chosen['target']
                unique_id = scene['id'] + '/' + skill
                require(unique_id not in example_ids, 'Duplicate curriculum example')
                example_ids.add(unique_id)
                row = sum(counts[partition].values())
                record = {'messages': [{'role': 'system', 'content': instructions},
                                       {'role': 'user', 'content': canonical(request)},
                                       {'role': 'assistant', 'content': canonical(response)}]}
                meta = {'row': row, 'matchId': match_id, 'heroId': scene['heroId'],
                        'episodeId': scene['retrospectiveOutcome']['id'], 'sourceSceneId': scene['id'],
                        'sourceHash': scene['sourceHash'], 'competencyId': skill, 'exampleId': unique_id,
                        'request': request, 'target': response, 'humanReviewed': False,
                        'provisionalPolicyTarget': True, 'trainingEligible': False}
                data, metadata = writers[partition]
                data.write(canonical(record) + '\n')
                metadata.write((',' if row else '') + canonical(meta))
                counts[partition][skill] += 1
                role_counts[partition][str(chosen['role'])] += 1
                phase_counts[partition][chosen['phase']] += 1
                state_counts[partition][skill + '/' + chosen['state']] += 1

    try:
        for split, file in files:
            partition = allowed[split]
            active = None
            candidates = defaultdict(dict)
            for scene in iter_jsonl(file):
                require(isinstance(scene, dict) and isinstance(scene.get('id'), str), 'Invalid source scene')
                pieces = scene['id'].split('/')
                require(len(pieces) == 3, 'Invalid source scene identity')
                match_id = pieces[0]
                require(match_id in manifest and manifest[match_id]['split'] == split and scene['split'] == split,
                        'Source partition mismatch')
                require(scene['sourceHash'] == manifest[match_id]['sourceHash'], 'Source hash mismatch')
                require(scene['id'] == f"{match_id}/{scene['heroId']}/{scene['retrospectiveOutcome']['id']}", 'Source identity mismatch')
                require(scene['id'] not in seen_ids, 'Duplicate input scene')
                seen_ids.add(scene['id'])
                sources_read[partition] += 1
                if active != match_id:
                    flush(active, partition, candidates)
                    active, candidates = match_id, defaultdict(dict)
                anchor = scene['input']['atOrBeforeAnchor']['anchorSeconds']
                phase = 'before-10m' if anchor < 600 else '10m-to-25m' if anchor < 1500 else 'after-25m'
                for skill in COMPETENCIES:
                    request = project_request(scene, skill, policy_version=policy_version)
                    response = expected_response(request)
                    require(all(len(response[k]) <= 700 for k in ['exercise', 'limitations']), 'Oversized target prose')
                    state = state_for(skill, response)
                    key = (scene['role']['position'], phase, state)
                    order = digest(scene['id'] + '/' + skill)
                    candidate = {'scene': scene, 'request': request, 'target': response,
                                 'role': key[0], 'phase': phase, 'state': state, 'order': order}
                    if key not in candidates[skill] or order < candidates[skill][key]['order']:
                        candidates[skill][key] = candidate
            flush(active, partition, candidates)
        expected_games = {partition: {m for m, row in manifest.items() if row['split'] in {s for s, p in allowed.items() if p == partition}}
                          for partition in ['train', 'validation']}
        for partition in writers:
            require(games[partition] == expected_games[partition], 'Source match coverage incomplete')
        require(not (games['train'] & games['validation']), 'Match leakage')
        for partition, (data, metadata) in writers.items():
            metadata.write(']\n')
            data.close()
            metadata.close()
            for suffix in ['.jsonl', '-metadata.json']:
                (output / (stem(partition) + suffix + '.tmp')).rename(output / (stem(partition) + suffix))
        (output / 'instructions.txt').write_text(instructions, encoding='utf-8')
        result = {'schemaVersion': 1, 'profile': 'competency-review-v1', 'reviewPolicyVersion': policy_version, 'perSkillMatchCap': cap,
                  'competencies': list(COMPETENCIES), 'sourceScenesRead': dict(sources_read),
                  'examples': {k: sum(v.values()) for k, v in counts.items()},
                  'matches': {k: len(v) for k, v in games.items()},
                  'byCompetency': dict(counts), 'byRole': dict(role_counts), 'byPhase': dict(phase_counts),
                  'byState': dict(state_counts), 'humanApproved': False, 'trainingExecuted': False,
                  'testOpened': False, 'labelOrigin': 'assistant-authored provisional evidence-bound policy',
                  'sourceFiles': {str(file.relative_to(source)): file_hash(file) for _, file in files},
                  'outputFiles': {p.name: file_hash(p) for p in output.iterdir() if p.is_file()}}
        (output / 'build-audit.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        return result
    finally:
        for data, metadata in writers.values():
            data.close()
            metadata.close()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path, help='New directory; never overwrites an existing dataset')
    parser.add_argument('--per-skill-match-cap', type=int, default=6)
    parser.add_argument('--policy-version', type=int, choices=[1, 2], default=1)
    args = parser.parse_args(argv)
    try:
        result = build(args.source, args.output, args.per_skill_match_cap, args.policy_version)
        print(json.dumps({key: result[key] for key in ['profile', 'examples', 'matches', 'byCompetency', 'trainingExecuted']}))
    except (AuditError, OSError, ValueError, KeyError, TypeError) as exc:
        print(json.dumps({'error': str(exc), 'trainingExecuted': False}), file=sys.stderr)
        return 2
    return 0


if __name__ == '__main__':
    sys.exit(main())
