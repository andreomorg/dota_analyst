"""Deterministic content agreement with provisional policy targets, never tactical certification."""
import argparse
from collections import Counter
from decimal import Decimal
from itertools import zip_longest
from pathlib import Path
import re
import sys

from ..core.json import canonical, parse_json
from ..core.streams import iter_jsonl, iter_json_array
from ..core.validation import require, AuditError
from ..hashing import file_hash
from .contracts import validate_candidate
from .facts import iter_candidate_prose
from .signal_wording import details_for

# Ignore digits embedded in identifiers (R1, v5.2); preserve sign, decimals and percentages.
NUMBER = re.compile(r'(?<![\w.])[-+]?\d+(?:[.,]\d+)?%?(?!\w)')


def numbers(text):
    return [Decimal(token.rstrip('%').replace(',', '.')) for token in NUMBER.findall(text)]


def normalized(text):
    return ' '.join(text.casefold().split())


def actions(candidate):
    return [normalized(a['action']) for a in candidate['alternatives']]


def rule_of(candidate):
    if candidate['disposition'] == 'insufficient':
        return 'R0'
    opportunity = normalized(candidate['opportunity'])
    matched = [f'R{i}' for i, (_, a, b, _, _) in enumerate(details_for(lambda *args: ''), 1)
               if normalized(a) in opportunity and normalized(b) in opportunity]
    return matched[0] if len(matched) == 1 else None


def scene_numbers(value, field=''):
    if type(value) in (int, float):
        yield Decimal(str(value))
        if field in ('healthShare', 'manaShare', 'towerSeenShare', 'allySeenShare'):
            yield Decimal(f'{value * 100:.0f}')
    elif isinstance(value, dict):
        for key, child in value.items():
            yield from scene_numbers(child, key)
    elif isinstance(value, list):
        for child in value:
            yield from scene_numbers(child, field)


def score_content(request, target, text, truncated=False, expected_rule=None):
    validate_candidate(request, target)
    expected_rule = expected_rule or rule_of(target)
    require(expected_rule in {f'R{i}' for i in range(12)}, 'Unknown target rule')
    base = {'rule': expected_rule, 'valid': False, 'ruleCorrect': False, 'actionsCorrect': False,
            'numbersCorrect': False, 'contentCorrect': False, 'inventedNumbers': []}
    if truncated:
        return {**base, 'error': 'truncated'}
    try:
        candidate = validate_candidate(request, parse_json(text))
    except (AuditError, KeyError, TypeError, ValueError) as error:
        return {**base, 'error': str(error)}
    allowed = set(scene_numbers([f['value'] for f in request['facts']]))
    allowed.update(n for prose in iter_candidate_prose(target) for n in numbers(prose))
    cited = [n for prose in iter_candidate_prose(candidate) for n in numbers(prose)]
    invented = sorted(set(cited) - allowed)
    rule_correct = rule_of(candidate) == expected_rule
    actions_correct = actions(candidate) == actions(target)
    numbers_correct = (Counter(numbers(candidate['opportunity'])) == Counter(numbers(target['opportunity']))
                       and not invented)
    return {**base, 'valid': True, 'ruleCorrect': rule_correct, 'actionsCorrect': actions_correct,
            'numbersCorrect': numbers_correct,
            'contentCorrect': rule_correct and actions_correct and numbers_correct,
            'inventedNumbers': [str(n) for n in invented], 'error': None}


def summarize(rows):
    rows = list(rows)
    require(bool(rows), 'Empty content evaluation')
    def counts(items):
        return {'cases': len(items), **{key: sum(bool(r[key]) for r in items)
                for key in ('valid', 'ruleCorrect', 'actionsCorrect', 'numbersCorrect', 'contentCorrect')},
                'correctRuleAndNumbers': sum(r['ruleCorrect'] and r['numbersCorrect'] for r in items),
                'inventedNumberCases': sum(bool(r['inventedNumbers']) for r in items)}
    total = counts(rows)
    rule_share = total['ruleCorrect'] / total['cases']
    numeric_share = total['correctRuleAndNumbers'] / total['ruleCorrect'] if total['ruleCorrect'] else 0
    json_share = total['valid'] / total['cases']
    r0_invented = sum(r['rule'] == 'R0' and bool(r['inventedNumbers']) for r in rows)
    return {**total, 'ruleAccuracy': rule_share, 'numberAccuracyGivenCorrectRule': numeric_share,
            'jsonValidity': json_share, 'r0InventedNumberCases': r0_invented,
            'byRule': {rule: counts([r for r in rows if r['rule'] == rule]) for rule in sorted({r['rule'] for r in rows})},
            'passed': rule_share >= .8 and numeric_share >= .9 and json_share >= .98 and r0_invented == 0,
            'tacticalQualityApproved': False}


def evaluate_content(root):
    root = Path(root)
    metadata = list(iter_json_array(root / 'validation-metadata.json'))
    names = ['validation-metadata.json', 'output/predictions-base.jsonl', 'output/predictions-adapted.jsonl']
    fingerprints = {name: file_hash(root / name) for name in names}
    result = {'schemaVersion': 1, 'kind': 'tactical-content-agreement', 'sourceFiles': fingerprints,
              'tacticalQualityApproved': False, 'variants': {}}
    for variant in ('base', 'adapted'):
        rows, sentinel = [], object()
        for index, (meta, prediction) in enumerate(zip_longest(metadata,
                iter_jsonl(root / 'output' / f'predictions-{variant}.jsonl'), fillvalue=sentinel)):
            require(meta is not sentinel and prediction is not sentinel, 'Incomplete content predictions')
            require(type(meta.get('row')) is int and meta['row'] == index and prediction.get('row') == index
                    and prediction.get('variant') == variant, 'Content prediction order mismatch')
            for key in ('matchId', 'heroId', 'episodeId', 'exampleId'):
                require(key in meta and type(prediction.get(key)) is type(meta[key])
                        and prediction[key] == meta[key], 'Content prediction identity mismatch: ' + key)
            require(type(prediction.get('hitTokenLimit')) is bool and type(prediction.get('text')) is str,
                    'Invalid content prediction')
            rows.append(score_content(meta['request'], meta['target'], prediction['text'],
                                      prediction['hitTokenLimit'], meta.get('policyRule')))
        result['variants'][variant] = {'summary': summarize(rows), 'cases': rows}
    require(fingerprints == {name: file_hash(root / name) for name in names}, 'Content evaluation sources changed')
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--experiment', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        require(not args.output.exists(), 'Content report must be new')
        result = evaluate_content(args.experiment)
        with args.output.open('x', encoding='utf-8') as stream:
            stream.write(canonical(result) + '\n')
        print(canonical({'report': str(args.output), 'variants': {
            name: value['summary'] for name, value in result['variants'].items()}}))
        return 0
    except (AuditError, OSError, ValueError, KeyError, TypeError) as error:
        print(canonical({'error': str(error), 'tacticalQualityApproved': False}), file=sys.stderr)
        return 2
