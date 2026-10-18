import io
import json
from pathlib import Path
import tempfile
import unittest
from contextlib import redirect_stdout

from coach_ml.audit import AuditError, main, training_status
from coach_ml.hashing import file_hash


class ProgressTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.output = self.root / 'output'
        self.output.mkdir()

    def checkpoint(self):
        (self.root / 'freeze.json').write_text('{}')
        (self.root / 'config.json').write_text('{"samples":100}')
        folder = self.output / 'checkpoint-000005'
        folder.mkdir()
        for name in ['state.pt', 'adapter_model.safetensors', 'adapter_config.json']:
            (folder / name).write_bytes(b'not-deserialized-by-status')
        pointer = {'directory': folder.name, 'cursor': 50, 'step': 5,
                   'freezeHash': file_hash(self.root / 'freeze.json')}
        (self.output / 'latest-checkpoint.json').write_text(json.dumps(pointer))
        return pointer

    def test_zeroed_progress_reports_checkpoint_separately_without_claiming_live_training(self):
        self.checkpoint()
        status = self.root / 'live-status.json'
        status.write_bytes(b'\0' * 270)
        report = training_status(self.root)
        self.assertFalse(report['statusIntegrityPassed'])
        self.assertEqual(report['warnings'], ['progress:null-bytes'])
        self.assertIsNone(report['percentCompleted'])
        self.assertEqual(report['checkpoint']['percentAtCheckpoint'], 50)
        self.assertFalse(report['checkpoint']['contentsValidated'])
        self.assertFalse(report['processStateVerified'])
        self.assertEqual(status.read_bytes(), b'\0' * 270)

    def test_missing_empty_truncated_and_oversized_files_have_controlled_diagnostics(self):
        path = self.root / 'live-status.json'
        for raw, reason in [(None, 'missing'), (b'', 'empty'), (b'{', 'invalid-json'),
                            (b'[]', 'not-object'), (b' ' * 131073, 'oversized')]:
            with self.subTest(reason=reason):
                if raw is not None:
                    path.write_bytes(raw)
                report = training_status(self.root)
                self.assertIn('progress:' + reason, report['warnings'])
                self.assertFalse(report['trainingResultAvailable'])

    def test_status_cli_returns_diagnostic_json_and_nonzero_for_corruption(self):
        (self.root / 'live-status.json').write_bytes(b'\0')
        output = io.StringIO()
        with redirect_stdout(output):
            code = main(['status', '--experiment', str(self.root)])
        self.assertEqual(code, 2)
        self.assertEqual(json.loads(output.getvalue())['schemaVersion'], 2)

    def test_wrong_freeze_or_missing_checkpoint_file_is_not_offered_as_available(self):
        self.checkpoint()
        (self.root / 'freeze.json').write_text('{"changed":true}')
        self.assertEqual(training_status(self.root)['checkpoint']['reason'], 'freeze-mismatch')
        (self.root / 'freeze.json').write_text('{}')
        (self.output / 'checkpoint-000005/state.pt').unlink()
        self.assertEqual(training_status(self.root)['checkpoint']['reason'], 'missing-checkpoint-file')

    def test_checkpoint_traversal_and_boolean_counters_are_rejected(self):
        pointer = self.checkpoint()
        for change in [{'directory': '../secret'}, {'cursor': True}, {'step': 4}]:
            with self.subTest(change=change):
                (self.output / 'latest-checkpoint.json').write_text(json.dumps({**pointer, **change}))
                self.assertEqual(training_status(self.root)['checkpoint']['reason'], 'invalid-reference')

    def test_corrupt_training_result_does_not_override_valid_progress(self):
        (self.root / 'live-status.json').write_text('{"status":"training","cursor":20,"totalSamples":100}')
        for raw in [b'\0', b'{"samples":true}']:
            with self.subTest(raw=raw):
                (self.output / 'training-result.json').write_bytes(raw)
                report = training_status(self.root)
                self.assertFalse(report['trainingResultAvailable'])
                self.assertEqual(report['percentCompleted'], 20)

    def test_invalid_progress_counts_still_fail_instead_of_fabricating_percentage(self):
        (self.root / 'live-status.json').write_text('{"cursor":101,"totalSamples":100}')
        with self.assertRaisesRegex(AuditError, 'counters'):
            training_status(self.root)

    def test_zeroed_evaluation_is_not_reported_as_available(self):
        (self.root / 'live-status.json').write_text('{"status":"evaluating"}')
        (self.root / 'evaluation-audit.json').write_bytes(b'\0')
        report = training_status(self.root)
        self.assertFalse(report['evaluationAuditAvailable'])
        self.assertIn('evaluation-audit:null-bytes', report['warnings'])

    def test_checkpoint_beyond_declared_sample_count_is_rejected(self):
        self.checkpoint()
        (self.root / 'config.json').write_text('{"samples":25}')
        self.assertEqual(training_status(self.root)['checkpoint']['reason'], 'invalid-counters')

    def test_completed_result_preserves_100_percent_but_never_claims_process_state(self):
        (self.root / 'live-status.json').write_text('{"status":"training"}')
        (self.output / 'training-result.json').write_text('{"samples":100}')
        report = training_status(self.root)
        self.assertEqual(report['percentCompleted'], 100)
        self.assertTrue(report['trainingResultAvailable'])
        self.assertFalse(report['processStateVerified'])

    def test_larger_paired_evaluation_report_has_its_own_bounded_read_budget(self):
        (self.root / 'live-status.json').write_text('{"status":"complete-content-review-pending"}')
        (self.root / 'evaluation-audit.json').write_text(json.dumps({'summary': [], 'byMatch': 'x' * 131073}))
        report = training_status(self.root)
        self.assertTrue(report['evaluationAuditAvailable'])
        self.assertTrue(report['statusIntegrityPassed'])
