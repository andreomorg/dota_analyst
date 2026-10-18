"""Bounded-memory hashes for corpora, checkpoints and model weights."""
import hashlib
from pathlib import Path


def file_hash(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()
