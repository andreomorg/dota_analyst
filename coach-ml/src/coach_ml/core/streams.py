"""Streaming JSON metadata and JSONL readers."""
import json
from pathlib import Path
from .json import DECODER, parse_json
from .validation import AuditError, require

def iter_json_array(file, chunk_size=65536):
    """Stream a metadata array, retaining at most a chunk plus the current object."""
    with Path(file).open(encoding="utf-8-sig") as stream:
        buffer, position, eof = "", 0, False

        def more():
            nonlocal buffer, position, eof
            fragment = stream.read(chunk_size)
            buffer = buffer[position:] + fragment
            position = 0
            eof = not fragment

        def peek():
            nonlocal position
            while True:
                while position < len(buffer) and buffer[position].isspace():
                    position += 1
                if position < len(buffer):
                    return buffer[position]
                if eof:
                    return None
                more()

        require(peek() == "[", "Metadata must be a JSON array")
        position += 1
        if peek() != "]":
            while True:
                require(peek() is not None, "Unterminated metadata array")
                while True:
                    try:
                        value, end = DECODER.raw_decode(buffer, position)
                        if end == len(buffer) and not eof:
                            more()
                            continue
                        position = end
                        break
                    except json.JSONDecodeError as exc:
                        if eof:
                            raise AuditError("Invalid metadata JSON") from exc
                        more()
                yield value
                separator = peek()
                if separator == "]":
                    break
                require(separator == ",", "Expected comma or closing metadata bracket")
                position += 1
                require(peek() != "]", "Trailing comma in metadata")
        require(peek() == "]", "Unterminated metadata array")
        position += 1
        require(peek() is None, "Trailing data after metadata array")


def iter_jsonl(file):
    with Path(file).open(encoding="utf-8-sig") as stream:
        for index, line in enumerate(stream):
            require(bool(line.strip()), f"Empty JSONL row {index}")
            try:
                yield parse_json(line)
            except AuditError as exc:
                raise AuditError(f"JSONL row {index}: {exc}") from exc
