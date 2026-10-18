"""Record installed versions for reproducible future runtime fingerprints."""
from importlib import metadata
import platform
import re
import sys

from .audit import require


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
