"""Record installed versions for reproducible future runtime fingerprints."""
from importlib import metadata
import platform
import re
import sys
from pathlib import Path

from .core.validation import require
from .hashing import file_hash


def runtime_code_hashes(root=None):
    """Include feature subpackages in new experiment fingerprints."""
    root = Path(root) if root is not None else Path(__file__).parent
    files = sorted(root.rglob('*.py'))
    require(bool(files), 'Runtime source tree is empty')
    require(all(not path.is_symlink() for path in files), 'Unsafe runtime source file')
    return {path.relative_to(root).as_posix(): file_hash(path) for path in files}


def runtime_environment(distributions=None):
    versions = {}
    installed = metadata.distributions() if distributions is None else distributions
    for distribution in installed:
        name = distribution.metadata.get('Name')
        require(isinstance(name, str) and bool(name.strip()), 'Installed distribution has no name')
        name = re.sub(r'[-_.]+', '-', name).lower()
        version = distribution.version
        require(isinstance(version, str) and bool(version), 'Installed distribution has no version')
        require(name not in versions or versions[name] == version,
                'Conflicting installed distribution versions: ' + name)
        versions[name] = version
    return {'python': sys.version, 'system': platform.system(), 'machine': platform.machine(),
            'packages': dict(sorted(versions.items()))}
