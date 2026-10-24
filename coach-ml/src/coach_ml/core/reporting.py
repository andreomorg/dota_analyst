"""Publish complete canonical reports atomically without replacing an existing destination."""
import os
import tempfile
from pathlib import Path

from .json import canonical


def write_report(path: Path, value: object) -> None:
    path = Path(path)
    payload = canonical(value) + '\n'
    descriptor, name = tempfile.mkstemp(prefix=f'.{path.name}.', suffix='.tmp', dir=path.parent)
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, 'w', encoding='utf-8', newline='\n') as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        # Same-directory hard link publishes the finished inode with exclusive destination creation.
        # Unsupported filesystems fail closed; there is no overwrite or partial-copy fallback.
        os.link(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)
