"""Diagnose progress artifacts without inferring process liveness or loading weights."""
from datetime import datetime, timezone
from pathlib import Path
import re

from .audit import AuditError, parse_json, require
from .hashing import file_hash

MAX_STATUS_BYTES = 128 * 1024


def read_object(path, max_bytes=MAX_STATUS_BYTES):
    try:
        with Path(path).open('rb') as stream:
            raw = stream.read(max_bytes + 1)
        if len(raw) > max_bytes:
            return None, 'oversized'
        if not raw:
            return None, 'empty'
        if b'\x00' in raw:
            return None, 'null-bytes'
        value = parse_json(raw.decode('utf-8-sig'))
        return (value, None) if isinstance(value, dict) else (None, 'not-object')
    except FileNotFoundError:
        return None, 'missing'
    except (AuditError, UnicodeError):
        return None, 'invalid-json'
    except OSError:
        return None, 'unreadable'


def checkpoint_reference(root):
    output = root / 'output'
    pointer, error = read_object(output / 'latest-checkpoint.json')
    if error:
        return {'available': False, 'reason': error, 'contentsValidated': False}
    directory, cursor, step = pointer.get('directory'), pointer.get('cursor'), pointer.get('step')
    if not (isinstance(directory, str) and re.fullmatch(r'checkpoint-[0-9]{6,8}', directory)
            and type(cursor) is int and cursor >= 0 and type(step) is int and step >= 0
            and directory == f'checkpoint-{step:06d}'
            and isinstance(pointer.get('freezeHash'), str) and re.fullmatch(r'[0-9a-f]{64}', pointer['freezeHash'])):
        return {'available': False, 'reason': 'invalid-reference', 'contentsValidated': False}
    try:
        folder = (output / directory).resolve()
        if not folder.is_relative_to(output.resolve()):
            return {'available': False, 'reason': 'outside-output', 'contentsValidated': False}
        if file_hash(root / 'freeze.json') != pointer['freezeHash']:
            return {'available': False, 'reason': 'freeze-mismatch', 'contentsValidated': False}
        for name in ['state.pt', 'adapter_model.safetensors', 'adapter_config.json']:
            artifact = folder / name
            if not artifact.resolve().is_relative_to(output.resolve()) or not artifact.is_file() or artifact.stat().st_size == 0:
                return {'available': False, 'reason': 'missing-checkpoint-file', 'contentsValidated': False}
    except OSError:
        return {'available': False, 'reason': 'unreadable-checkpoint', 'contentsValidated': False}
    config, _ = read_object(root / 'config.json')
    total = config.get('samples') if config else None
    if type(total) is int and total > 0 and cursor > total:
        return {'available': False, 'reason': 'invalid-counters', 'contentsValidated': False}
    if type(total) is not int or total <= 0:
        total = None
    return {'available': True, 'reason': None, 'directory': directory, 'cursor': cursor, 'step': step,
            'totalSamples': total, 'percentAtCheckpoint': cursor / total * 100 if total else None,
            'freezeHashMatched': True, 'contentsValidated': False}


def training_status(experiment):
    root = Path(experiment)
    progress_file = root / 'live-status.json'
    progress, progress_error = read_object(progress_file)
    result, result_error = read_object(root / 'output/training-result.json')
    evaluation, evaluation_error = read_object(root / 'evaluation-audit.json', max_bytes=16 * 1024 * 1024)
    warnings = []
    if progress_error:
        warnings.append('progress:' + progress_error)
    if result_error and result_error != 'missing':
        warnings.append('training-result:' + result_error)
    if evaluation_error and evaluation_error != 'missing':
        warnings.append('evaluation-audit:' + evaluation_error)
    progress = progress or {}
    done, total = progress.get('cursor'), progress.get('totalSamples')
    if result is not None:
        samples = result.get('samples')
        if type(samples) is int and samples > 0:
            done = total = samples
        else:
            result = None
            result_error = 'invalid-samples'
            warnings.append('training-result:invalid-samples')
    percent = None
    if done is not None or total is not None:
        require(type(done) is int and type(total) is int and total > 0 and 0 <= done <= total,
                'Invalid progress counters')
        percent = done / total * 100
    try:
        modified = datetime.fromtimestamp(progress_file.stat().st_mtime, timezone.utc).isoformat()
    except OSError:
        modified = None
    integrity = (progress_error is None and result_error in [None, 'missing']
                 and evaluation_error in [None, 'missing'])
    return {'schemaVersion': 2, 'kind': 'training-file-status', 'reportedState': progress.get('status'),
            'samplesCompleted': done, 'totalSamples': total, 'percentCompleted': percent,
            'step': progress.get('step'), 'totalSteps': progress.get('totalSteps'),
            'trainingResultAvailable': result is not None,
            'evaluationAuditAvailable': evaluation is not None,
            'lastProgressWriteUtc': modified, 'statusIntegrityPassed': integrity, 'warnings': warnings,
            'checkpoint': checkpoint_reference(root),
            'processStateVerified': False, 'tacticalImprovementValidated': False}
