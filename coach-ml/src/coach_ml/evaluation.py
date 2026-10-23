"""Compare saved predictions with provisional targets, without an ML runtime."""
import argparse
import json
import os
from itertools import zip_longest
from pathlib import Path
import sys

from .core.validation import AuditError, require
from .core.json import canonical, parse_json
from .core.streams import iter_json_array, iter_jsonl
from .audit import check_factual_target
from .comparison import PairedComparison, failure_reasons
from .hashing import file_hash


def score_response(text, expected):
    require(isinstance(expected, dict) and isinstance(expected.get('observation'), dict)
            and all(isinstance(expected.get(k), str) and expected[k].strip() and len(expected[k]) <= 700
                    for k in ['reviewFocus', 'exercise', 'limitations'])
            and isinstance(expected.get('evidenceIds'), list) and bool(expected['evidenceIds'])
            and all(isinstance(ref, str) for ref in expected['evidenceIds'])
            and len(set(expected['evidenceIds'])) == len(expected['evidenceIds']),
            'Unsupported evaluation target contract')
    try:
        value = parse_json(text)
    except AuditError:
        return {'valid': False, 'observationCorrect': False, 'focusCorrect': False, 'exactPolicy': False}
    valid = (isinstance(value, dict) and set(value) == set(expected)
             and isinstance(value.get('observation'), dict)
             and all(isinstance(value.get(key), str) and 0 < len(value[key].strip()) and len(value[key]) <= 700
                     for key in ['reviewFocus', 'exercise', 'limitations'])
             and isinstance(value.get('evidenceIds'), list)
             and all(isinstance(ref, str) for ref in value['evidenceIds'])
             and len(set(value['evidenceIds'])) == len(value['evidenceIds'])
             and set(value['evidenceIds']) == set(expected['evidenceIds'])
             and ('competencyId' not in expected or value.get('competencyId') == expected['competencyId']))
    return {'valid': bool(valid),
            'observationCorrect': bool(valid and canonical(value['observation']) == canonical(expected['observation'])),
            'focusCorrect': bool(valid and value['reviewFocus'] == expected['reviewFocus']),
            'exactPolicy': canonical(value) == canonical(expected)}


def evaluate(experiment):
    lock = Path(experiment) / '.evaluation.lock'
    with lock.open('x', encoding='utf-8') as stream:
        stream.write(str(os.getpid()))
    try:
        return _evaluate(experiment)
    finally:
        lock.unlink()


def _evaluate(experiment):
    root = Path(experiment)
    config = root / 'config.json'
    if config.exists() and parse_json(config.read_text(encoding='utf-8-sig')).get('profile') in ('tactical-review-v1', 'tactical-review-v5', 'tactical-review-v52'):
        from .tactical.evaluation import evaluate_tactical
        return evaluate_tactical(root)
    destination = root / 'evaluation-audit.json'
    if destination.exists():
        raise AuditError('Evaluation already exists; preserve it and use a separate report for a new comparison')
    cases = root / 'evaluation-cases.jsonl'
    require(not cases.exists(), 'Case report already exists; preserve it and use a new directory')
    case_temp = root / 'evaluation-cases.jsonl.tmp'
    report_temp = destination.with_suffix('.json.tmp')
    require(not report_temp.exists(), 'Partial evaluation report exists; preserve it for investigation')
    inputs = ['validation-metadata.json', 'output/predictions-base.jsonl', 'output/predictions-adapted.jsonl']
    source_hashes = {name: file_hash(root / name) for name in inputs}
    # Exclusive creation preserves a partial artifact from any earlier attempt.
    stream = case_temp.open('x', encoding='utf-8')
    try:
        with stream:
            result = _score(root, stream)
        require(source_hashes == {name: file_hash(root / name) for name in inputs}, 'Evaluation inputs changed during scoring')
        result['sourceFiles'] = source_hashes
        result['caseReport'] = {'file': cases.name, 'sha256': file_hash(case_temp),
                                'cases': result['pairedComparison']['overall']['cases']}
        report_temp.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        os.replace(case_temp, cases)
        os.replace(report_temp, destination)
        return result
    finally:
        case_temp.unlink(missing_ok=True)


def _score(root, cases_stream):
    summary = []
    games = set()
    base_cases = []
    paired = PairedComparison()
    missing = object()
    metric_keys = ['strictValid', 'normalizedValid', 'strictObservationCorrect',
                   'normalizedObservationCorrect', 'strictFocusCorrect', 'exactPolicy', 'truncated']
    for variant in ['base', 'adapted']:
        counts = {'variant': variant, 'cases': 0, **dict.fromkeys(metric_keys, 0), 'byCompetency': {}}
        rows = iter_jsonl(root / 'output' / f'predictions-{variant}.jsonl')
        metadata = iter_json_array(root / 'validation-metadata.json')
        identities = set()
        for index, (row, meta) in enumerate(zip_longest(rows, metadata, fillvalue=missing)):
            require(row is not missing and meta is not missing, 'Incomplete prediction set')
            require(isinstance(meta, dict) and type(meta.get('row')) is int and meta['row'] == index,
                    'Metadata identity mismatch')
            require(isinstance(meta.get('request'), dict) and isinstance(meta.get('target'), dict)
                    and isinstance(meta.get('matchId'), str) and bool(meta['matchId'])
                    and type(meta.get('heroId')) is int and meta['heroId'] > 0
                    and isinstance(meta.get('episodeId'), str) and bool(meta['episodeId']),
                    'Invalid evaluation metadata')
            require(isinstance(row, dict) and type(row.get('row')) is int and row['row'] == index and row.get('variant') == variant
                    and row.get('matchId') == meta['matchId'] and row.get('episodeId') == meta['episodeId'],
                    'Prediction identity mismatch')
            skill = meta.get('competencyId', 'factual-review')
            require(isinstance(skill, str) and bool(skill), 'Invalid competency identity')
            identity = meta.get('exampleId', f"{meta['matchId']}/{meta.get('heroId')}/{meta['episodeId']}")
            require(isinstance(identity, str) and identity not in identities, 'Duplicate evaluation identity')
            identities.add(identity)
            if 'competencyId' in meta:
                require(identity == f"{meta['matchId']}/{meta['heroId']}/{meta['episodeId']}/{skill}"
                        and meta['request'].get('heroId') == meta['heroId']
                        and meta['request'].get('competencyId') == skill,
                        'Metadata competency identity mismatch')
                require(row.get('exampleId') == identity and row.get('heroId') == meta.get('heroId')
                        and type(row.get('heroId')) is int and row.get('competencyId') == skill,
                        'Prediction competency identity mismatch')
                from .competencies import expected_response
                require(canonical(meta['target']) == canonical(expected_response(meta['request'])),
                        'Evaluation target differs from competency policy')
            else:
                # Reject legacy qualitative targets before interpreting scores.
                require(isinstance(meta['target'].get('observation'), dict), 'Unsupported evaluation target contract')
                require('competencyId' not in meta['target'] and 'competencyId' not in meta['request'],
                        'Missing competency metadata')
                check_factual_target(meta['request'], meta['target'], index)
            require(type(row.get('hitTokenLimit')) is bool, 'Missing truncation state')
            require(isinstance(row.get('text'), str), 'Missing prediction text')
            strict = score_response(row['text'], meta['target'])
            text = row['text'].strip()
            lines = text.splitlines()
            if len(lines) >= 3 and lines[0] in ['```', '```json'] and lines[-1] == '```':
                text = '\n'.join(lines[1:-1])
            normalized = score_response(text, meta['target'])
            if row['hitTokenLimit']:
                strict = dict.fromkeys(strict, False)
                normalized = dict.fromkeys(normalized, False)
            group = counts['byCompetency'].setdefault(skill, {'cases': 0, **dict.fromkeys(metric_keys, 0)})
            counts['cases'] += 1
            group['cases'] += 1
            games.add(meta['matchId'])
            scores = {}
            for key, metric in [('strictValid', strict['valid']), ('normalizedValid', normalized['valid']),
                                ('strictObservationCorrect', strict['observationCorrect']),
                                ('normalizedObservationCorrect', normalized['observationCorrect']),
                                ('strictFocusCorrect', strict['focusCorrect']), ('exactPolicy', strict['exactPolicy'])]:
                counts[key] += int(metric)
                group[key] += int(metric)
                scores[key] = bool(metric)
            counts['truncated'] += int(row.get('hitTokenLimit') is True)
            group['truncated'] += int(row['hitTokenLimit'])
            scores['truncated'] = row['hitTokenLimit']
            assessed = {'scores': scores, 'failureReasons': failure_reasons(scores)}
            if variant == 'base':
                base_cases.append((identity, assessed))
            else:
                require(index < len(base_cases) and base_cases[index][0] == identity, 'Paired evaluation identity mismatch')
                before = base_cases[index][1]
                paired.add(meta['matchId'], skill, before['scores'], scores)
                case = {'row': index, 'matchId': meta['matchId'], 'heroId': meta.get('heroId'),
                        'episodeId': meta['episodeId'], 'exampleId': identity, 'competencyId': skill,
                        'base': before, 'adapted': assessed}
                cases_stream.write(canonical(case) + '\n')
        require(counts['cases'] > 0, 'Empty evaluation metadata')
        summary.append(counts)
    result = {'schemaVersion': 2, 'summary': summary, 'validationGames': len(games),
              'pairedComparison': paired.report(),
              'labelOrigin': 'provisional targets; check dataset review origin',
              'contentReviewPending': True, 'tacticalImprovementValidated': False,
              'humanApproved': False, 'promoted': False}
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml evaluate', description=__doc__)
    parser.add_argument('--experiment', required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        print(json.dumps(evaluate(args.experiment), ensure_ascii=False, indent=2))
        return 0
    except (AuditError, OSError, ValueError, KeyError, TypeError) as exc:
        print(json.dumps({'error': str(exc), 'promoted': False}), file=sys.stderr)
        return 2
