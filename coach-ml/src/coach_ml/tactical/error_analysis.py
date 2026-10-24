"""Explain existing F3 failures without changing its score or interpreting free prose as field bindings."""
import argparse
import sys
from collections import Counter
from pathlib import Path

from ..core.input_snapshot import verify_inputs
from ..core.json import canonical, parse_json
from ..core.reporting import write_report
from ..core.streams import iter_json_array, iter_jsonl
from ..core.validation import AuditError, require
from .content_evaluation import actions, evaluate_content, numbers, rule_of, score_content


def explain(request, target, text, truncated=False, expected_rule=None):
    score = score_content(request, target, text, truncated, expected_rule)
    categories = []
    result = {'score': score, 'categories': categories, 'observedRule': None,
              'fieldAttribution': 'unavailable-free-prose', 'expectedSignalFields': {}}
    signals = next((fact['value'] for fact in request['facts'] if fact['id'] == 'signals'), [])
    if signals:
        result['expectedSignalFields'] = signals[0]['numbers']
        result['fieldAttribution'] = 'ambiguous-free-prose'
    if not score['valid']:
        if truncated:
            categories.append('truncated')
        else:
            try:
                parse_json(text)
                categories.append('invalid-contract')
            except (AuditError, ValueError, TypeError):
                categories.append('invalid-json')
        return result
    candidate = parse_json(text)
    observed_rule = rule_of(candidate)
    result['observedRule'] = observed_rule
    if not score['ruleCorrect']:
        categories.append('rule-indeterminate' if observed_rule is None else 'wrong-rule')
    expected_actions, observed_actions = actions(target), actions(candidate)
    result['actionDifferences'] = [
        {'alternative': label, 'expected': expected_actions[i] if i < len(expected_actions) else None,
         'observed': observed_actions[i] if i < len(observed_actions) else None}
        for i, label in enumerate(('A', 'B'))
        if (expected_actions[i:i+1] != observed_actions[i:i+1])]
    if not score['actionsCorrect']:
        categories.append('wrong-actions')
    expected, observed = Counter(numbers(target['opportunity'])), Counter(numbers(candidate['opportunity']))
    result['numbers'] = {'expected': [str(n) for n in numbers(target['opportunity'])],
                         'observed': [str(n) for n in numbers(candidate['opportunity'])],
                         'missing': [str(n) for n in (expected - observed).elements()],
                         'extra': [str(n) for n in (observed - expected).elements()]}
    if expected - observed:
        categories.append('missing-numbers')
    if observed - expected:
        categories.append('extra-numbers')
    if score['inventedNumbers']:
        categories.append('invented-numbers')
    return result


def analyze(root):
    root = Path(root)
    reference = evaluate_content(root)  # Existing F3 owns all alignment, identity and score checks.
    metadata = list(iter_json_array(root / 'validation-metadata.json'))
    variants = {}
    for variant in ('base', 'adapted'):
        cases = []
        for meta, prediction, expected in zip(metadata, iter_jsonl(root / 'output' / f'predictions-{variant}.jsonl'),
                                              reference['variants'][variant]['cases'], strict=True):
            detail = explain(meta['request'], meta['target'], prediction['text'], prediction['hitTokenLimit'],
                             meta.get('policyRule'))
            require(detail['score'] == expected, 'Error analysis diverged from F3')
            cases.append({'row': meta['row'], **{key: meta[key] for key in
                          ('matchId', 'heroId', 'episodeId', 'exampleId')}, **detail})
        counts = Counter(category for case in cases for category in case['categories'])
        confusion = {}
        for case in cases:
            expected_rule = case['score']['rule']
            observed = (case['observedRule'] or 'indeterminate') if case['score']['valid'] else (
                'truncated' if 'truncated' in case['categories'] else 'invalid')
            confusion.setdefault(expected_rule, Counter())[observed] += 1
        rules = sorted({case['score']['rule'] for case in cases})
        variants[variant] = {'summary': reference['variants'][variant]['summary'], 'categories': dict(counts),
                             'ruleConfusion': {rule: dict(sorted(values.items())) for rule, values in sorted(confusion.items())},
                             'byRule': {rule: {'cases': sum(c['score']['rule'] == rule for c in cases),
                                 'categories': dict(Counter(category for c in cases if c['score']['rule'] == rule
                                                            for category in c['categories']))} for rule in rules},
                             'cases': cases}
    verify_inputs({name: root / name for name in reference['sourceFiles']}, reference['sourceFiles'],
                  'Error analysis sources changed')
    return {'schemaVersion': 1, 'kind': 'tactical-error-analysis', 'sourceFiles': reference['sourceFiles'],
            'variants': variants, 'tacticalQualityApproved': False, 'trainingExecuted': False}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--experiment', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        require(not args.output.exists(), 'Error report must be new')
        result = analyze(args.experiment)
        write_report(args.output, result)
        print(canonical({'report': str(args.output), 'tacticalQualityApproved': False}))
        return 0
    except (AuditError, OSError, ValueError, KeyError, TypeError) as error:
        print(canonical({'error': str(error)}), file=sys.stderr)
        return 2
