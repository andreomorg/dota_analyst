"""Validate experiment boundaries before importing the optional training stack."""
import argparse
import json
import math
import os
from pathlib import Path
import re
import sys

from .audit import AuditError, parse_json, require
from .datasets import audit_experiment


def load_config(experiment):
    cfg = parse_json((Path(experiment) / 'config.json').read_text(encoding='utf-8-sig'))
    require(isinstance(cfg, dict), 'Expected a training configuration object')
    require(isinstance(cfg.get('model'), str) and re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', cfg['model']), 'Expected an organization/model ID')
    require(isinstance(cfg.get('revision'), str) and re.fullmatch(r'[0-9a-f]{40}', cfg['revision']), 'Expected a pinned model commit')
    require(type(cfg.get('epochs')) is int and cfg['epochs'] == 1, 'This runtime supports one explicit epoch')
    for name, maximum in [('samples', 10_000_000), ('validationSamples', 100_000), ('microBatch', 64),
                           ('accumulation', 4096), ('loraRank', 128), ('loraAlpha', 512),
                           ('checkpointSteps', 100_000), ('maxTrainingTokens', 32768),
                           ('maxNewTokens', 8192), ('inferenceBatch', 64)]:
        require(type(cfg.get(name)) is int and 1 <= cfg[name] <= maximum, f'Invalid configuration: {name}')
    for name, maximum in [('learningRate', 0.01), ('warmupFraction', 1.0)]:
        value = cfg.get(name)
        require(type(value) in [int, float] and math.isfinite(value) and 0 < value <= maximum, f'Invalid configuration: {name}')
    require(type(cfg.get('gradientCheckpointing')) is bool, 'Expected gradientCheckpointing boolean')
    require(type(cfg.get('seed')) is int and 0 <= cfg['seed'] < 2**32, 'Invalid random seed')
    modules = cfg.get('targetModules')
    require(isinstance(modules, list) and bool(modules) and all(isinstance(m, str) and m for m in modules)
            and len(set(modules)) == len(modules), 'Invalid LoRA target modules')
    require(cfg.get('profile', 'factual-review') in ['factual-review', 'competency-review-v1'], 'Unknown training profile')
    return cfg


def inspect_experiment(experiment, model_cache, manifest):
    root, cache = Path(experiment).resolve(), Path(model_cache).resolve()
    cfg = load_config(root)
    require((root / 'instructions.txt').is_file() and (root / 'experimental-authorization.json').is_file(),
            'Missing instructions or experimental authorization record')
    authorization = parse_json((root / 'experimental-authorization.json').read_text(encoding='utf-8-sig'))
    require(isinstance(authorization, dict) and isinstance(authorization.get('scope'), str)
            and bool(authorization['scope'].strip()), 'Experimental authorization needs a scope')
    snapshot = cache / 'hub' / ('models--' + cfg['model'].replace('/', '--')) / 'snapshots' / cfg['revision']
    require(snapshot.is_dir() and snapshot.resolve().is_relative_to(cache), 'Pinned local model snapshot is missing or outside cache')
    profile = cfg.get('profile', 'factual-review')
    joint = audit_experiment(root, manifest, profile)
    require(joint['inputIsolationPassed'], 'Duplicate model inputs within or across training/validation partitions')
    reports = joint['data']
    for name, partition, count in [('development', 'train', cfg['samples']), ('validation', 'validation', cfg['validationSamples'])]:
        require(reports[partition]['rows'] == count, f'Configured sample count differs from {name} data')
    return {'model': cfg['model'], 'revision': cfg['revision'], 'snapshot': str(snapshot), 'profile': profile,
            'data': reports, 'inputIsolationPassed': True, 'gpuInitialized': False, 'optimizerUpdated': False}


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml train', description=__doc__)
    parser.add_argument('--experiment', required=True, type=Path)
    parser.add_argument('--model-cache', required=True, type=Path)
    parser.add_argument('--manifest', required=True, type=Path)
    parser.add_argument('--stage', choices=['inspect', 'prepare', 'run'], required=True)
    args = parser.parse_args(argv)
    root = args.experiment.resolve()
    try:
        require(__debug__, 'Optimized Python disables runtime checks; run without -O or PYTHONOPTIMIZE')
        if args.stage == 'prepare':
            require(not (root / 'freeze.json').exists(), 'Experiment already frozen; use a new directory')
        elif args.stage == 'run':
            frozen = parse_json((root / 'freeze.json').read_text(encoding='utf-8-sig'))
            require(isinstance(frozen, dict) and frozen.get('runtimePackage') == 'coach-ml',
                    'This experiment belongs to a different runtime; preserve its original launcher')
        report = inspect_experiment(root, args.model_cache, args.manifest)
        if args.stage == 'inspect':
            print(json.dumps(report, ensure_ascii=False, indent=2))
            return 0
        lock = root / '.coach-ml.lock'
        with lock.open('x', encoding='utf-8') as stream:
            stream.write(json.dumps({'pid': os.getpid(), 'stage': args.stage}))
        try:
            os.environ['HF_HUB_OFFLINE'] = '1'
            os.environ['TRANSFORMERS_OFFLINE'] = '1'
            os.environ.setdefault('TOKENIZERS_PARALLELISM', 'false')
            try:
                from .lora import run
                run(args.stage, root, args.model_cache.resolve())
            except ImportError as exc:
                raise AuditError('Training dependencies unavailable; install the train extra in a compatible ML environment') from exc
            except AssertionError as exc:
                raise AuditError('Training invariant failed: ' + (str(exc) or 'runtime validation')) from exc
            if args.stage == 'run' and not (root / 'evaluation-audit.json').exists():
                from .evaluation import evaluate
                evaluate(root)
            if args.stage == 'run':
                require((root / 'output/training-result.json').is_file(), 'Missing training result')
                progress = root / 'live-status.json'
                temp = progress.with_suffix('.json.tmp')
                temp.write_text(json.dumps({'status': 'complete-content-review-pending',
                    'trainingComplete': True, 'comparisonComplete': True, 'promoted': False}), encoding='utf-8')
                os.replace(temp, progress)
        finally:
            lock.unlink()
        return 0
    except (AuditError, OSError, ValueError, RuntimeError) as exc:
        print(json.dumps({'error': str(exc), 'promoted': False}), file=sys.stderr)
        return 2
