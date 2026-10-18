"""Joint dataset checks; separate match IDs do not guarantee distinct model inputs."""
import argparse
import json
from pathlib import Path
import sys

from .audit import AuditError, audit_sft, digest, iter_json_array


def audit_experiment(experiment, manifest, profile='factual-review'):
    root = Path(experiment)
    reports, signatures, matches, duplicates = {}, {}, {}, {}
    for partition, stem in [('train', 'development'), ('validation', 'validation')]:
        reports[partition] = audit_sft(root / (stem + '.jsonl'), root / (stem + '-metadata.json'),
                                       manifest, partition, profile)
        seen, games, repeated = set(), set(), 0
        for meta in iter_json_array(root / (stem + '-metadata.json')):
            signature = digest(meta['request'])
            repeated += int(signature in seen)
            seen.add(signature)
            games.add(meta['matchId'])
        signatures[partition], matches[partition], duplicates[partition] = seen, games, repeated
    overlap = len(signatures['train'] & signatures['validation'])
    match_overlap = len(matches['train'] & matches['validation'])
    passed = not (overlap or match_overlap or any(duplicates.values()))
    return {'schemaVersion': 1, 'kind': 'joint-dataset-audit', 'profile': profile,
            'data': reports, 'duplicateInputs': duplicates, 'sharedInputsTrainValidation': overlap,
            'sharedMatchesTrainValidation': match_overlap, 'inputIsolationPassed': passed,
            'structuralIntegrityPassed': True, 'gpuInitialized': False,
            'sourceReplaysOpened': False, 'trainingEligibilityGranted': False,
            'tacticalQualityApproved': False}


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml audit-experiment', description=__doc__)
    parser.add_argument('--experiment', required=True, type=Path)
    parser.add_argument('--manifest', required=True, type=Path)
    parser.add_argument('--profile', choices=['factual-review', 'competency-review-v1'], default='factual-review')
    args = parser.parse_args(argv)
    try:
        report = audit_experiment(args.experiment, args.manifest, args.profile)
        print(json.dumps(report, ensure_ascii=False, indent=2))
        return 0 if report['inputIsolationPassed'] else 2
    except (AuditError, OSError, UnicodeError) as exc:
        print(json.dumps({'error': str(exc), 'inputIsolationPassed': False}), file=sys.stderr)
        return 2
