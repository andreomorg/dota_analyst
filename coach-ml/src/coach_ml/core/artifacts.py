"""Bounded JSON artifact diagnostics; never deserialize tensors."""
from pathlib import Path

from .json import parse_json
from .validation import AuditError

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
