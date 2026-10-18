"""CLI transport for offline experiment use cases; existing imports remain available."""
import argparse
import json
from pathlib import Path
import sys
import os
from .core.validation import AuditError
from .experiments.preparation import inspect_experiment, load_config
from .experiments.application import execute
from .experiments.backend import run_lora

def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml train', description=__doc__)
    parser.add_argument('--experiment', required=True, type=Path)
    parser.add_argument('--model-cache', required=True, type=Path)
    parser.add_argument('--manifest', required=True, type=Path)
    parser.add_argument('--stage', choices=['inspect', 'prepare', 'run'], required=True)
    parser.add_argument('--run-store', type=Path, default=os.environ.get('COACH_ML_RUN_STORE'),
                        help='Shared local SQLite run store; required for cooperative pause and global GPU ownership')
    args = parser.parse_args(argv)
    try:
        report = execute(args.stage, args.experiment, args.model_cache, args.manifest, run_lora, args.run_store)
        if report is not None:
            print(json.dumps(report, ensure_ascii=False, indent=2))
        return 0
    except (AuditError, OSError, ValueError, RuntimeError) as exc:
        print(json.dumps({'error': str(exc), 'promoted': False}), file=sys.stderr)
        return 2
