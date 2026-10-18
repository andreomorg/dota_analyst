"""Offline experiment orchestration behind a lazy training backend port."""
import json
import os
from pathlib import Path
from typing import Protocol
from ..core.json import parse_json
from ..core.validation import AuditError, require
from .preparation import inspect_experiment
from .control import TrainingPaused
from .runs import RunStore
from ..core.json import digest
from ..hashing import file_hash

class TrainingBackend(Protocol):
    def __call__(self, stage: str, experiment: Path, model_cache: Path): ...

def execute(stage, experiment, model_cache, manifest, backend: TrainingBackend, run_store=None):
    root = Path(experiment).resolve()
    require(stage in ('inspect', 'prepare', 'run'), 'Unknown experiment stage')
    require(__debug__, 'Optimized Python disables runtime checks; run without -O or PYTHONOPTIMIZE')
    if stage == 'prepare':
        require(not (root / 'freeze.json').exists(), 'Experiment already frozen; use a new directory')
    elif stage == 'run':
        frozen = parse_json((root / 'freeze.json').read_text(encoding='utf-8-sig'))
        require(isinstance(frozen, dict) and frozen.get('runtimePackage') == 'coach-ml',
                'This experiment belongs to a different runtime; preserve its original launcher')
    report = inspect_experiment(root, model_cache, manifest)
    if stage == 'inspect':
        return report
    lock = root / '.coach-ml.lock'
    lock_stream = lock.open('x', encoding='utf-8')
    managed = None
    keys = ('COACH_ML_RUN_STORE', 'COACH_ML_RUN_ID', 'COACH_ML_ATTEMPT')
    previous_environment = {key: os.environ.get(key) for key in keys}
    try:
        with lock_stream as stream:
            stream.write(json.dumps({'pid': os.getpid(), 'stage': stage}))
        if run_store:
            store = RunStore(run_store)
            run_id = digest({'experiment': str(root), 'stage': stage})
            identity = file_hash(root / ('freeze.json' if stage == 'run' else 'config.json'))
            initial_state = 'preparing' if stage == 'prepare' else 'running'
            attempt = store.start(run_id, root, identity, initial_state=initial_state)
            managed = store, run_id, attempt
            os.environ.update(dict(zip(keys, (str(run_store), run_id, attempt))))
            print(json.dumps({'runId': run_id, 'attemptId': attempt, 'state': initial_state}), flush=True)
        os.environ['HF_HUB_OFFLINE'] = '1'
        os.environ['TRANSFORMERS_OFFLINE'] = '1'
        os.environ.setdefault('TOKENIZERS_PARALLELISM', 'false')
        try:
            backend(stage, root, Path(model_cache).resolve())
        except ImportError as exc:
            raise AuditError('Training dependencies unavailable; install the train extra in a compatible ML environment') from exc
        except AssertionError as exc:
            raise AuditError('Training invariant failed: ' + (str(exc) or 'runtime validation')) from exc
        if stage == 'run' and not (root / 'evaluation-audit.json').exists():
            from .capabilities import finalize_candidate
            finalize_candidate(root)
            from .control import phase
            phase('evaluating')
            from ..evaluation import evaluate
            evaluate(root)
        if stage == 'run':
            require((root / 'output/training-result.json').is_file(), 'Missing training result')
            progress = root / 'live-status.json'
            temp = progress.with_suffix('.json.tmp')
            temp.write_text(json.dumps({'status': 'complete-content-review-pending',
                'trainingComplete': True, 'comparisonComplete': True, 'promoted': False}), encoding='utf-8')
            os.replace(temp, progress)
        if managed:
            store.transition(run_id, attempt, 'completed' if stage == 'run' else 'prepared')
    except TrainingPaused:
        require(managed is not None and stage == 'run', 'Cannot acknowledge an unmanaged pause')
        from ..checkpoints import verify_checkpoint
        pointer = parse_json((root / 'output/latest-checkpoint.json').read_text(encoding='utf-8'))
        verify_checkpoint(root / 'output', pointer, file_hash(root / 'freeze.json'))
        paused = {'status': 'paused', 'runId': run_id, 'checkpoint': pointer, 'promoted': False}
        temp = root / 'live-status.json.tmp'
        temp.write_text(json.dumps(paused), encoding='utf-8')
        os.replace(temp, root / 'live-status.json')
        store.transition(run_id, attempt, 'paused', checkpoint=pointer)
        return paused
    except BaseException:
        if managed:
            store.transition(run_id, attempt, 'failed', detail='Worker exited with an error; inspect local evidence')
        raise
    finally:
        lock_stream.close()
        lock.unlink()
        for key, value in previous_environment.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value

    return None
