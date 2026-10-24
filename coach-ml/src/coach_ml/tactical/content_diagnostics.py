"""Paired diagnostics over existing F3 results, without changing scoring or acceptance."""
import argparse
import sys
from pathlib import Path

from ..core.input_snapshot import capture_inputs, verify_inputs
from ..core.json import canonical, parse_json
from ..core.reporting import write_report
from ..core.validation import AuditError, require

CRITERIA = ('valid', 'ruleCorrect', 'actionsCorrect', 'numbersCorrect', 'contentCorrect')


def diagnose(report):
    require(type(report) is dict and type(report.get('schemaVersion')) is int and report['schemaVersion'] == 1
            and report.get('kind') == 'tactical-content-agreement', 'Expected an F3 content report')
    variants = report.get('variants')
    require(type(variants) is dict and set(variants) == {'base', 'adapted'}, 'Expected paired base/adapted variants')
    require(all(type(variant) is dict for variant in variants.values()), 'Malformed content variant')
    base, adapted = (variants[name].get('cases') for name in ('base', 'adapted'))
    require(type(base) is list and type(adapted) is list and len(base) == len(adapted) and base,
            'Incomplete paired content cases')
    pairs = []
    for index, (before, after) in enumerate(zip(base, adapted, strict=False)):
        require(all(type(row) is dict and type(row.get('rule')) is str and row['rule'] in {f'R{i}' for i in range(12)}
                    and all(type(row.get(key)) is bool for key in CRITERIA) for row in (before, after)),
                'Malformed content case')
        require(before['rule'] == after['rule'], 'Paired target rule mismatch')
        pairs.append((index, before, after))

    def counts(items):
        result = {'cases': len(items), 'criteria': {}}
        for key in CRITERIA:
            gains = [index for index, b, a in items if not b[key] and a[key]]
            regressions = [index for index, b, a in items if b[key] and not a[key]]
            result['criteria'][key] = {
                'baseCorrect': sum(b[key] for _, b, _ in items),
                'adaptedCorrect': sum(a[key] for _, _, a in items),
                'gainedRows': gains, 'regressedRows': regressions,
                'stableCorrect': sum(b[key] and a[key] for _, b, a in items),
                'stableIncorrect': sum(not b[key] and not a[key] for _, b, a in items),
                'netChange': len(gains) - len(regressions)}
        return result

    return {'schemaVersion': 1, 'kind': 'tactical-content-paired-diagnostics', **counts(pairs),
            'byRule': {rule: counts([p for p in pairs if p[1]['rule'] == rule])
                       for rule in sorted({b['rule'] for _, b, _ in pairs})},
            'tacticalQualityApproved': False, 'trainingExecuted': False}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        require(not args.output.exists(), 'Diagnostics output must be new')
        files = {'report': args.report}
        hashes = capture_inputs(files)
        result = diagnose(parse_json(args.report.read_text(encoding='utf-8-sig')))
        verify_inputs(files, hashes, 'F3 report changed during reading')
        result['sourceReportSha256'] = hashes['report']
        write_report(args.output, result)
        print(canonical({'report': str(args.output), 'cases': result['cases'], 'tacticalQualityApproved': False}))
        return 0
    except (AuditError, OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        print(canonical({'error': str(error), 'tacticalQualityApproved': False}), file=sys.stderr)
        return 2
