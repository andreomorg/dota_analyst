"""Measure dataset coverage without treating repeated tasks as new replay scenes."""
import argparse
from collections import defaultdict
import json
from pathlib import Path
import sys

from .core.validation import AuditError, require
from .core.streams import iter_json_array
from .datasets import audit_experiment
from .hashing import file_hash


def phase_for(anchor):
    require(type(anchor) is int and anchor >= 0, 'Invalid coverage anchor')
    return 'before-10m' if anchor < 600 else '10m-to-25m' if anchor < 1500 else 'after-25m'


class Bucket:
    def __init__(self):
        self.examples = 0
        self.scenes = set()
        self.matches = set()

    def add(self, meta):
        self.examples += 1
        self.scenes.add(meta['sourceSceneId'])
        self.matches.add(meta['matchId'])

    def report(self):
        return {'examples': self.examples, 'playerScenes': len(self.scenes), 'matches': len(self.matches)}


def audit_coverage(experiment, manifest, profile='factual-review'):
    root, manifest = Path(experiment), Path(manifest)
    names = ['development.jsonl', 'development-metadata.json', 'validation.jsonl', 'validation-metadata.json']
    hashes = {name: file_hash(root / name) for name in names}
    manifest_hash = file_hash(manifest)
    audited = audit_experiment(root, manifest, profile)
    require(audited['inputIsolationPassed'], 'Coverage requires isolated inputs; audit/deduplicate the dataset first')
    partitions, cells = {}, {}
    for partition, stem in [('train', 'development'), ('validation', 'validation')]:
        overall = Bucket()
        axes = {axis: defaultdict(Bucket) for axis in ['hero', 'role', 'phase', 'competency', 'roleOrigin']}
        matrix = defaultdict(Bucket)
        patch_absent = 0
        for meta in iter_json_array(root / (stem + '-metadata.json')):
            request = meta['request']
            phase = phase_for(request['evidence']['event']['anchorSeconds'])
            skill = meta.get('competencyId', 'factual-review')
            hero, role = meta['heroId'], request['role']['position']
            declared = request['role'].get('isInferred')
            origin = 'declared-inferred' if declared is True else 'declared-not-inferred' if declared is False else 'unspecified'
            overall.add(meta)
            for axis, value in [('hero', str(hero)), ('role', str(role)), ('phase', phase),
                                ('competency', skill), ('roleOrigin', origin)]:
                axes[axis][value].add(meta)
            matrix[(hero, role, phase, skill)].add(meta)
            patch_absent += int('patch' not in request and 'patch' not in meta)
        cells[partition] = matrix
        partitions[partition] = {**overall.report(),
            'dimensions': {axis: {key: bucket.report() for key, bucket in sorted(groups.items())}
                           for axis, groups in axes.items()},
            'examplesWithoutProjectedPatch': patch_absent,
            'cells': [{'heroId': key[0], 'role': key[1], 'phase': key[2], 'competencyId': key[3], **bucket.report()}
                      for key, bucket in sorted(matrix.items())]}
    unseen = set(cells['validation']) - set(cells['train'])
    unseen_scenes, unseen_matches = set(), set()
    for key in unseen:
        unseen_scenes.update(cells['validation'][key].scenes)
        unseen_matches.update(cells['validation'][key].matches)
    train_heroes = {key[0] for key in cells['train']}
    validation_heroes = {key[0] for key in cells['validation']}
    require(hashes == {name: file_hash(root / name) for name in names}
            and manifest_hash == file_hash(manifest), 'Inputs changed during coverage audit')
    return {'schemaVersion': 1, 'kind': 'dataset-coverage', 'profile': profile, 'partitions': partitions,
            'trainHeroes': len(train_heroes), 'validationHeroes': len(validation_heroes),
            'validationOnlyHeroes': sorted(validation_heroes - train_heroes),
            'trainOnlyHeroes': sorted(train_heroes - validation_heroes),
            'validationCellsAbsentFromTrain': [
                {'heroId': key[0], 'role': key[1], 'phase': key[2], 'competencyId': key[3],
                 **cells['validation'][key].report()} for key in sorted(unseen)],
            'unseenValidationExamples': sum(cells['validation'][key].examples for key in unseen),
            'unseenValidationPlayerScenes': len(unseen_scenes), 'unseenValidationMatches': len(unseen_matches),
            'sceneIdentity': 'match/hero/episode; different heroes are different player perspectives',
            'inputIsolationPassed': True, 'patchCoverageAnalyzed': False,
            'sourceFiles': hashes, 'manifestSha256': manifest_hash,
            'sourceReplaysOpened': False, 'modelWeightsLoaded': False, 'trainingExecuted': False,
            'tacticalQualityApproved': False, 'trainingEligibilityGranted': False}


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml coverage', description=__doc__)
    parser.add_argument('--experiment', required=True, type=Path)
    parser.add_argument('--manifest', required=True, type=Path)
    parser.add_argument('--profile', choices=['factual-review', 'competency-review-v1'], default='factual-review')
    parser.add_argument('--output', type=Path, help='New report file; print only a concise summary when supplied')
    args = parser.parse_args(argv)
    try:
        require(args.output is None or not args.output.exists(), 'Coverage report already exists; use a new file')
        report = audit_coverage(args.experiment, args.manifest, args.profile)
        if args.output is None:
            print(json.dumps(report, ensure_ascii=False, indent=2))
        else:
            with args.output.open('x', encoding='utf-8') as stream:
                json.dump(report, stream, ensure_ascii=False, indent=2)
                stream.write('\n')
            summary = {key: report[key] for key in ['kind', 'trainHeroes', 'validationHeroes',
                'trainOnlyHeroes', 'validationOnlyHeroes', 'unseenValidationExamples', 'unseenValidationPlayerScenes']}
            print(json.dumps({**summary, 'reportFile': str(args.output)}))
        return 0
    except (AuditError, OSError, ValueError, TypeError, KeyError) as exc:
        print(json.dumps({'error': str(exc), 'trainingExecuted': False}), file=sys.stderr)
        return 2
