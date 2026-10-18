import json
from pathlib import Path
import tempfile
import unittest

from coach_ml.core.validation import AuditError
from coach_ml.experiments.capabilities import require_compatible, record_promotion
from coach_ml.hashing import file_hash


class CapabilityTests(unittest.TestCase):
    def test_competency_adapter_is_not_an_assessments_engine(self):
        capability = {'schemaVersion': 1, 'task': 'competency-review-v1',
                      'inputSchema': 'competency-review-v1:request-v1',
                      'outputSchema': 'competency-review-v1:response-v1'}
        with self.assertRaisesRegex(AuditError, 'incompatible'):
            require_compatible(capability, 'http-review', 'review-context-v1', 'assessments-v1')

    def test_promotion_needs_bound_artifacts_tactical_approval_and_rollback(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            capability = root / 'candidate.json'
            evaluation = root / 'evaluation.json'
            decision = root / 'decision.json'
            output = root / 'promotion.json'
            value = {'schemaVersion': 1, 'task': 'task', 'inputSchema': 'in', 'outputSchema': 'out',
                     'adapterFiles': {'adapter_model.safetensors': 'a'*64}, 'freezeSha256': 'b'*64}
            capability.write_text(json.dumps(value))
            evaluation.write_text(json.dumps({'structuralIntegrityPassed': True, 'tacticalQualityApproved': False}))
            approval = {'task': 'task', 'inputSchema': 'in', 'outputSchema': 'out', 'approved': True,
                        'reviewer': 'test-only reviewer', 'reason': 'synthetic gate test', 'rollbackModel': 'previous',
                        'capabilitySha256': file_hash(capability), 'evaluationSha256': file_hash(evaluation)}
            decision.write_text(json.dumps(approval))
            with self.assertRaisesRegex(AuditError, 'tactical approval'):
                record_promotion(capability, evaluation, decision, output)
            self.assertFalse(output.exists())
            evaluation.write_text(json.dumps({'structuralIntegrityPassed': True, 'tacticalQualityApproved': True}))
            with self.assertRaisesRegex(AuditError, 'identity mismatch'):
                record_promotion(capability, evaluation, decision, output)
            approval['evaluationSha256'] = file_hash(evaluation)
            decision.write_text(json.dumps(approval))
            result = record_promotion(capability, evaluation, decision, output)
            self.assertFalse(result['installed'])
            with self.assertRaises(FileExistsError):
                record_promotion(capability, evaluation, decision, output)
