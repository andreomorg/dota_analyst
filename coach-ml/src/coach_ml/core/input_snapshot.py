"""Content snapshots for read-only audits; eligibility remains the caller's responsibility."""
from collections.abc import Mapping
from pathlib import Path

from ..hashing import file_hash
from .validation import require


def capture_inputs(files: Mapping[str, Path]) -> dict[str, str]:
    return {name: file_hash(path) for name, path in files.items()}


def verify_inputs(files: Mapping[str, Path], expected: Mapping[str, str], message: str) -> None:
    require(capture_inputs(files) == expected, message)
