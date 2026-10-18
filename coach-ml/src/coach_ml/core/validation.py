"""Shared validation errors, independent of audits and ML."""

class AuditError(ValueError):
    pass

def require(condition, message):
    if not condition:
        raise AuditError(message)
