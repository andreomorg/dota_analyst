"""Copy completed training predictions for an additional, isolated CPU evaluation."""
import argparse
import json
from pathlib import Path
import shutil
import sys

from .audit import AuditError, canonical, require
from .evaluation import evaluate
from .hashing import file_hash
from .progress import read_object


def snapshot_evaluation(source, output):
    source, output = Path(source).resolve(), Path(output).resolve()
    require(source != output and not output.exists(), 'Output must be a new directory')
    require(not output.is_relative_to(source), 'Output must be outside the source directory')
    result, error = read_object(source / 'output/training-result.json')
    require(error is None and type(result.get('samples')) is int and result['samples'] > 0,
            'Completed training result is not available; do not snapshot an active or incomplete run')
    names = ['validation-metadata.json', 'output/predictions-base.jsonl',
             'output/predictions-adapted.jsonl', 'output/training-result.json']
    if (source / 'config.json').exists():
        config, error = read_object(source / 'config.json')
        require(error is None and type(config.get('samples')) is int and config['samples'] == result['samples'],
                'Training result sample count differs from configuration')
        names.append('config.json')
    require(all((source / name).is_file() for name in names), 'Required prediction files are missing')
    hashes = {name: file_hash(source / name) for name in names}
    output.mkdir(parents=True, exist_ok=False)
    (output / 'output').mkdir()
    for name in names:
        shutil.copyfile(source / name, output / name)
        require(file_hash(output / name) == hashes[name], 'Snapshot copy hash mismatch: ' + name)
    require(hashes == {name: file_hash(source / name) for name in names}, 'Source changed during snapshot')
    copied_result, error = read_object(output / 'output/training-result.json')
    require(error is None and canonical(copied_result) == canonical(result), 'Training result changed before copying')
    if 'config.json' in names:
        copied_config, error = read_object(output / 'config.json')
        require(error is None and canonical(copied_config) == canonical(config), 'Configuration changed before copying')
    comparison = evaluate(output)
    report = {'schemaVersion': 1, 'kind': 'isolated-evaluation-snapshot', 'source': str(source),
              'sourceFiles': hashes, 'sourceSamplesDeclared': result['samples'],
              'evaluationCases': comparison['caseReport']['cases'],
              'sourceModified': False, 'modelWeightsLoaded': False, 'trainingExecuted': False,
              'processStateVerified': False, 'tacticalImprovementValidated': False,
              'humanApproved': False, 'promoted': False,
              'outputFiles': {name: file_hash(output / name) for name in
                              [*names, 'evaluation-audit.json', 'evaluation-cases.jsonl']}}
    temporary = output / 'snapshot-audit.json.tmp'
    with temporary.open('x', encoding='utf-8') as stream:
        stream.write(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    temporary.rename(output / 'snapshot-audit.json')
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml snapshot-evaluation', description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        result = snapshot_evaluation(args.source, args.output)
        print(json.dumps({key: result[key] for key in ['kind', 'sourceSamplesDeclared', 'evaluationCases', 'sourceModified', 'promoted']}))
        return 0
    except (AuditError, OSError, ValueError, TypeError, KeyError) as exc:
        print(json.dumps({'error': str(exc), 'promoted': False}), file=sys.stderr)
        return 2
