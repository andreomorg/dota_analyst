"""Validate saved generation prefixes before loading an ML model or appending."""
from pathlib import Path

from .audit import parse_json, require

MAX_ROW_BYTES = 1024 * 1024


def prediction_prefix(path, metadata, variant):
    require(variant in ('base', 'adapted'), 'Invalid prediction variant')
    path = Path(path)
    require(not path.is_symlink(), 'Unsafe prediction file')
    if not path.exists():
        return 0
    require(path.is_file(), 'Unsafe prediction file')
    count = 0
    with path.open('rb') as stream:
        while True:
            raw = stream.readline(MAX_ROW_BYTES + 1)
            if not raw:
                break
            require(len(raw) <= MAX_ROW_BYTES, 'Prediction row exceeds size limit')
            require(raw.endswith(b'\n'), 'Incomplete prediction line; preserve file for investigation')
            require(raw.strip() and b'\x00' not in raw, 'Empty or damaged prediction line')
            require(count < len(metadata), 'Too many prediction rows')
            row = parse_json(raw.decode('utf-8'))
            require(isinstance(row, dict), 'Prediction row must be an object')
            expected = metadata[count]
            require(type(row.get('row')) is int and row['row'] == count
                    and row.get('variant') == variant, 'Prediction order or variant mismatch')
            for key in ('matchId', 'episodeId', 'heroId', 'exampleId', 'competencyId'):
                value, target = row.get(key), expected.get(key)
                require(type(value) is type(target) and value == target,
                        f'Prediction identity mismatch: {key}')
            require(isinstance(row.get('text'), str), 'Invalid prediction text')
            require(type(row.get('newTokens')) is int and row['newTokens'] > 0,
                    'Invalid prediction token count')
            require(type(row.get('hitTokenLimit')) is bool, 'Invalid prediction truncation flag')
            count += 1
    return count
