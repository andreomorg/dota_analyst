"""Measure the C# position estimator against the real positions of role-queue matches (work/agents/plan.md R3).

Reads the internal route position-estimates/{matchId} for each match of a manifest: no export, no training. For every
hero with a role-queue position, the estimator's answer at each checkpoint and the old "richest at 10:00" rule are
compared with it; the report gives the accuracy per checkpoint and per position and the confusion matrix (standard
library only)."""
import argparse
import json
import os
import sys
import time

from ..core.validation import AuditError, require
from .export import HttpFetcher, fetch_with_retry, read_manifest

POSITIONS = [1, 2, 3, 4, 5]


def measure(matches, fetch, interval=1.05, sleep=time.sleep, log=print):
    """The report over the matches; matches without a document, outdated or without role queue are only counted."""
    totals = {'matches': 0, 'withRoleQueue': 0, 'skipped': {}}
    checkpoints = {}
    old = {'correct': 0, 'total': 0}
    for index, match in enumerate(matches):
        if index:
            sleep(interval)
        body = fetch_with_retry(fetch, match['matchId'], sleep)
        if isinstance(body, str):
            # 'noDocument' or 'outdated'.
            totals['skipped'][body] = totals['skipped'].get(body, 0) + 1
            continue
        totals['matches'] += 1
        heroes = [hero for hero in body['heroes'] if hero.get('roleQueue') is not None]
        if not heroes:
            continue
        totals['withRoleQueue'] += 1
        for hero in heroes:
            truth = hero['roleQueue']
            if hero.get('oldRule') is not None:
                old['total'] += 1
                old['correct'] += hero['oldRule'] == truth
            for estimate in hero['estimates']:
                at = checkpoints.setdefault(str(estimate['second']), {
                    'correct': 0, 'total': 0, 'byPosition': {str(p): {'correct': 0, 'total': 0} for p in POSITIONS},
                    'confusion': {str(p): {str(q): 0 for q in POSITIONS} for p in POSITIONS}})
                hit = estimate['position'] == truth
                at['total'] += 1
                at['correct'] += hit
                at['byPosition'][str(truth)]['total'] += 1
                at['byPosition'][str(truth)]['correct'] += hit
                at['confusion'][str(truth)][str(estimate['position'])] += 1
        log(f'{match["matchId"]}: {len(heroes)} heroes')

    def rate(counts):
        return round(counts['correct'] / counts['total'], 3) if counts['total'] else None

    return totals | {
        'oldRuleAccuracy': rate(old),
        'checkpoints': {second: {'accuracy': rate(values), 'heroes': values['total'],
                                 'byPosition': {p: rate(c) for p, c in values['byPosition'].items()},
                                 'confusion': values['confusion']}
                        for second, values in sorted(checkpoints.items(), key=lambda item: int(item[0]))},
    }


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml position-measure')
    parser.add_argument('--manifest', required=True, help='manifest.json with the matches (any split)')
    parser.add_argument('--api-url', required=True, help='Local API base URL ending with /')
    parser.add_argument('--key-env', default='COACH_ML_API_KEY', help='Environment variable holding the machine key')
    parser.add_argument('--output', required=True, help='Report file (JSON)')
    parser.add_argument('--limit', type=int, help='Only the first N matches')
    args = parser.parse_args(argv)
    try:
        matches = read_manifest(args.manifest)[:args.limit]
        require(not os.path.exists(args.output), 'The report already exists')
        fetch = HttpFetcher(args.api_url, os.environ.get(args.key_env, ''), path='position-estimates')
        report = measure(matches, fetch)
        with open(args.output, 'w', encoding='utf-8') as stream:
            json.dump(report, stream, indent=2, sort_keys=True)
    except (AuditError, OSError) as exc:
        print(json.dumps({'error': str(exc)}), file=sys.stderr)
        return 2
    print(json.dumps({'matches': report['matches'], 'withRoleQueue': report['withRoleQueue'],
                      'oldRuleAccuracy': report['oldRuleAccuracy'],
                      'accuracy': {second: values['accuracy'] for second, values in report['checkpoints'].items()}}))
    return 0
