"""Seal local checkpoint artifacts before publication; no tensor imports."""
import json
from pathlib import Path
import re

from .audit import require
from .hashing import file_hash
from .progress import read_object

FILES = ('adapter_config.json', 'adapter_model.safetensors', 'state.pt')


def identity(pointer, freeze_hash):
    step, cursor = pointer.get('step'), pointer.get('cursor')
    require(type(step) is int and step > 0 and type(cursor) is int and cursor > 0,
            'Invalid checkpoint counters')
    require(pointer.get('directory') == f'checkpoint-{step:06d}'
            and re.fullmatch(r'checkpoint-[0-9]{6,8}', pointer['directory']),
            'Invalid checkpoint directory')
    require(isinstance(freeze_hash, str) and re.fullmatch(r'[0-9a-f]{64}', freeze_hash)
            and pointer.get('freezeHash') == freeze_hash, 'Checkpoint freeze mismatch')


def artifacts(folder):
    result = {}
    for name in FILES:
        path = folder / name
        require(not path.is_symlink() and path.is_file() and path.stat().st_size > 0,
                f'Missing or unsafe checkpoint artifact: {name}')
        result[name] = {'bytes': path.stat().st_size, 'sha256': file_hash(path)}
    return result


def seal_checkpoint(folder, pointer):
    folder = Path(folder)
    identity(pointer, pointer.get('freezeHash'))
    manifest = {'schemaVersion': 1, 'checkpoint': pointer, 'files': artifacts(folder)}
    # A partial directory remains evidence on failure; never overwrite an existing seal.
    with (folder / 'checkpoint-manifest.json').open('x', encoding='utf-8') as stream:
        json.dump(manifest, stream, indent=2)
        stream.flush()
        import os
        os.fsync(stream.fileno())


def verify_checkpoint(output, pointer, freeze_hash):
    require(isinstance(pointer, dict), 'Invalid checkpoint pointer')
    identity(pointer, freeze_hash)
    output = Path(output).resolve()
    folder = output / pointer['directory']
    require(not folder.is_symlink() and folder.resolve().parent == output,
            'Checkpoint outside output')
    manifest_path = folder / 'checkpoint-manifest.json'
    require(not manifest_path.is_symlink(), 'Unsafe checkpoint manifest')
    manifest, error = read_object(manifest_path)
    require(error is None and manifest.get('schemaVersion') == 1,
            'Missing or invalid checkpoint integrity manifest')
    require(manifest.get('checkpoint') == pointer, 'Checkpoint identity mismatch')
    require(manifest.get('files') == artifacts(folder), 'Checkpoint artifact hash mismatch')
    return folder
