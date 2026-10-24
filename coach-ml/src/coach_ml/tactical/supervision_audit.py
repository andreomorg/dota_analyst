"""Diagnose exact visible-input conflicts and reproducible policy labels, without editing datasets."""
import argparse
import sys
from collections import Counter, defaultdict
from itertools import zip_longest
from pathlib import Path

from ..core.input_snapshot import capture_inputs, verify_inputs
from ..core.json import canonical, digest, parse_json
from ..core.reporting import write_report
from ..core.streams import iter_json_array, iter_jsonl
from ..core.validation import AuditError, require
from ..hashing import file_hash
from .audit import validate_request
from .contracts import validate_candidate
from .facts import audit_candidate_facts


def audit(root):
    root = Path(root)
    cfg_path = root / 'config.json'
    cfg_hash = file_hash(cfg_path)
    cfg = parse_json(cfg_path.read_text(encoding='utf-8-sig'))
    profile = cfg.get('profile')
    require(profile in ('tactical-review-v5', 'tactical-review-v52'), 'Expected situated tactical profile')
    files = {'config.json': cfg_path}
    for split in ('development', 'validation'):
        for suffix in ('.jsonl', '-metadata.json'):
            files[split + suffix] = root / (split + suffix)
    hashes = capture_inputs(files)
    require(hashes['config.json'] == cfg_hash, 'Config changed during reading')
    groups, requests, issues, counts = defaultdict(list), defaultdict(set), [], {}
    for split, configured in (('development', 'samples'), ('validation', 'validationSamples')):
        sentinel, rows = object(), 0
        for index, (record, meta) in enumerate(zip_longest(iter_jsonl(root / (split + '.jsonl')),
                iter_json_array(root / (split + '-metadata.json')), fillvalue=sentinel)):
            require(record is not sentinel and meta is not sentinel, 'Dataset/metadata length mismatch')
            require(type(meta) is dict and type(meta.get('row')) is int and meta['row'] == index,
                    'Supervision metadata row mismatch')
            require(type(record) is dict, 'Expected SFT object')
            messages = record.get('messages')
            require(type(messages) is list and len(messages) == 3 and all(type(m) is dict for m in messages)
                    and [m.get('role') for m in messages] == ['system', 'user', 'assistant']
                    and all(type(m.get('content')) is str and m['content'].strip() for m in messages),
                    'Malformed supervision messages')
            request, target = parse_json(messages[1]['content']), parse_json(messages[2]['content'])
            require(canonical(request) == canonical(meta.get('request')) and canonical(target) == canonical(meta.get('target')),
                    'Supervision metadata does not match visible messages')
            require(type(meta.get('heroId')) is int and meta['heroId'] == request.get('heroId')
                    and all(type(meta.get(k)) is str and meta[k] for k in ('matchId', 'episodeId', 'exampleId')),
                    'Invalid supervision identity')
            ref = {'partition': split, 'row': index, **{k: meta[k] for k in ('matchId', 'heroId', 'episodeId', 'exampleId')}}
            def issue(category, detail, ref=ref):
                issues.append({**ref, 'category': category, 'detail': detail})
            from .pipeline import SYSTEM
            from .prompt_v52 import system_for
            if messages[0]['content'] != system_for(profile, SYSTEM):
                issue('system-prompt-mismatch', 'Visible system message differs from the declared profile')
            input_hash = digest({'profile': profile, 'messages': messages[:2]})
            groups[input_hash].append({**ref, 'targetSha256': digest(target)})
            requests[digest(request)].add(input_hash)
            rows += 1
            try:
                validate_request(request, profile)
            except AuditError as error:
                issue('invalid-source-request', str(error))
                continue
            try:
                validate_candidate(request, target)
            except (AuditError, KeyError, TypeError) as error:
                issue('unsupported-or-invalid-target', str(error))
                continue
            flags = audit_candidate_facts(request, target)['flags']
            if flags:
                issue('ambiguous-content-review', flags)
            if profile == 'tactical-review-v52':
                from .policy_v3 import audit_agreement as reproduce
                origin, version = 'deterministic-policy-v3', 3
            else:
                from .policy_v2 import target_for as reproduce
                origin, version = 'deterministic-policy-v2', 2
            if meta.get('labelOrigin') != origin or meta.get('policyVersion') != version:
                issue('non-reproducible-origin', 'No supported deterministic policy receipt; requires separate review')
            else:
                try:
                    expected_rule, expected_target = reproduce(request)
                    if expected_rule != meta.get('policyRule') or canonical(expected_target) != canonical(target):
                        issue('policy-target-mismatch', {'expectedRule': expected_rule, 'declaredRule': meta.get('policyRule')})
                except (AuditError, KeyError, TypeError, StopIteration) as error:
                    issue('non-reproducible-origin', str(error))
        require(type(cfg.get(configured)) is int and rows == cfg[configured], 'Configured supervision sample count mismatch')
        counts[split] = rows
    collisions = []
    for fingerprint, items in sorted(groups.items()):
        partitions = {item['partition'] for item in items}
        if len(partitions) > 1:
            collisions.append({'category': 'cross-partition-input', 'inputSha256': fingerprint, 'cases': items})
        for partition in sorted(partitions):
            subset = [item for item in items if item['partition'] == partition]
            if len(subset) > 1:
                category = 'supervision-conflict' if len({i['targetSha256'] for i in subset}) > 1 else 'duplicate-identical'
                collisions.append({'category': category, 'inputSha256': fingerprint, 'cases': subset})
    verify_inputs(files, hashes, 'Supervision sources changed')
    return {'schemaVersion': 1, 'kind': 'tactical-supervision-diagnostics', 'profile': profile, 'sourceFiles': hashes,
            'rows': counts, 'issues': issues, 'groups': collisions,
            'issueCounts': dict(Counter(issue['category'] for issue in issues)),
            'groupCounts': dict(Counter(group['category'] for group in collisions)),
            'sameRequestWithDifferentVisibleInputs': sum(len(inputs) > 1 for inputs in requests.values()),
            'limitations': ['Exact input only; different prompts or list order are different model inputs.',
                            'Lexical factual flags are review alerts, not proven contradictions.',
                            'No match-manifest verification; use audit-experiment for partition/source eligibility.',
                            'Policy agreement does not certify tactical truth.'],
            'trainingExecuted': False, 'examplesModified': False, 'tacticalQualityApproved': False}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--experiment', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        require(not args.output.exists(), 'Supervision report must be new')
        result = audit(args.experiment)
        write_report(args.output, result)
        print(canonical({'report': str(args.output), 'rows': result['rows'], 'issueCounts': result['issueCounts']}))
        return 0
    except (AuditError, OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        print(canonical({'error': str(error)}), file=sys.stderr)
        return 2
