"""Strict JSON decoding and canonical identities."""
import hashlib
import json
import math
from .validation import AuditError

def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise AuditError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def finite_float(value):
    number = float(value)
    if not math.isfinite(number):
        raise AuditError("Nonfinite JSON number")
    return number


def reject_constant(value):
    raise AuditError(f"Nonstandard JSON constant: {value}")


DECODER = json.JSONDecoder(object_pairs_hook=unique_object, parse_float=finite_float,
                           parse_constant=reject_constant)


def parse_json(text):
    try:
        return DECODER.decode(text)
    except (ValueError, RecursionError) as exc:
        raise AuditError(f"Invalid JSON: {exc}") from exc


def canonical(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=False, allow_nan=False,
                      separators=(",", ":"))


def digest(value):
    return hashlib.sha256(canonical(value).encode("utf-8")).hexdigest()
