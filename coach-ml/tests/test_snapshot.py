import json
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch

from coach_ml.audit import AuditError
from coach_ml.hashing import file_hash
from coach_ml.snapshot import snapshot_evaluation
import test_evaluation


class SnapshotTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source, self.output = self.root / 'source', self.root / 'snapshot'
        fixture = test_evaluation.EvaluationTests()
        fixture.setUp()
        shutil.copytree(fixture.root, self.source)
        fixture.temp.cleanup()
        (self.source / 'output/training-result.json').write_text('{"samples":100}')
        (self.source / 'config.json').write_text('{"samples":100}')

    def run_snapshot(self):
        return snapshot_evaluation(self.source, self.output)

    def source_hashes(self):
        return {str(p.relative_to(self.source)): file_hash(p) for p in self.source.rglob('*') if p.is_file()}

    def test_snapshot_evaluates_copies_and_preserves_every_source_file(self):
        before = self.source_hashes()
        report = self.run_snapshot()
        self.assertEqual(before, self.source_hashes())
        self.assertEqual(report['evaluationCases'], 2)
        self.assertFalse(report['sourceModified'])
        self.assertFalse(report['promoted'])
        self.assertTrue((self.output / 'snapshot-audit.json').is_file())
        for name, expected in report['outputFiles'].items():
            self.assertEqual(file_hash(self.output / name), expected)

    def test_active_training_without_result_is_rejected_before_output_creation(self):
        (self.source / 'output/training-result.json').unlink()
        with self.assertRaisesRegex(AuditError, 'Completed training result'):
            self.run_snapshot()
        self.assertFalse(self.output.exists())

    def test_boolean_result_samples_are_not_accepted_as_completed_training(self):
        (self.source / 'output/training-result.json').write_text('{"samples":true}')
        with self.assertRaisesRegex(AuditError, 'Completed training result'):
            self.run_snapshot()
        self.assertFalse(self.output.exists())

    def test_result_sample_count_must_match_source_configuration(self):
        (self.source / 'config.json').write_text('{"samples":101}')
        with self.assertRaisesRegex(AuditError, 'differs from configuration'):
            self.run_snapshot()

    def test_existing_output_is_not_overwritten(self):
        self.output.mkdir()
        sentinel = self.output / 'keep.txt'
        sentinel.write_text('keep')
        with self.assertRaisesRegex(AuditError, 'new directory'):
            self.run_snapshot()
        self.assertEqual(sentinel.read_text(), 'keep')

    def test_output_inside_source_is_rejected_to_preserve_the_original_tree(self):
        before = self.source_hashes()
        with self.assertRaisesRegex(AuditError, 'outside the source'):
            snapshot_evaluation(self.source, self.source / 'new-evaluation')
        self.assertEqual(before, self.source_hashes())
        self.assertFalse((self.source / 'new-evaluation').exists())

    def test_incomplete_predictions_never_publish_snapshot_success(self):
        file = self.source / 'output/predictions-adapted.jsonl'
        file.write_text(file.read_text().splitlines()[0] + '\n')
        with self.assertRaisesRegex(AuditError, 'Incomplete'):
            self.run_snapshot()
        self.assertFalse((self.output / 'snapshot-audit.json').exists())
        self.assertFalse((self.output / 'evaluation-audit.json').exists())

    def test_source_change_during_copy_is_detected_before_scoring(self):
        copy = shutil.copyfile

        def changed_copy(source, destination):
            result = copy(source, destination)
            if Path(source).name == 'validation-metadata.json':
                with Path(source).open('a') as file:
                    file.write(' ')
            return result

        with patch('coach_ml.snapshot.shutil.copyfile', side_effect=changed_copy):
            with self.assertRaisesRegex(AuditError, 'Source changed'):
                self.run_snapshot()
        self.assertFalse((self.output / 'evaluation-audit.json').exists())

    def test_partial_copy_is_detected_without_changing_source(self):
        before = self.source_hashes()

        def partial_copy(_source, destination):
            Path(destination).write_bytes(b'partial')

        with patch('coach_ml.snapshot.shutil.copyfile', side_effect=partial_copy):
            with self.assertRaisesRegex(AuditError, 'copy hash mismatch'):
                self.run_snapshot()
        self.assertEqual(before, self.source_hashes())
        self.assertFalse((self.output / 'snapshot-audit.json').exists())

    def test_legacy_target_rejection_does_not_rewrite_source_labels(self):
        file = self.source / 'validation-metadata.json'
        metadata = json.loads(file.read_text())
        metadata[0].pop('competencyId')
        metadata[0]['target'] = {'schemaVersion': 3, 'reflections': []}
        file.write_text(json.dumps(metadata))
        before = self.source_hashes()
        with self.assertRaisesRegex(AuditError, 'Unsupported evaluation target'):
            self.run_snapshot()
        self.assertEqual(before, self.source_hashes())
        self.assertFalse((self.output / 'snapshot-audit.json').exists())

    def test_completed_markers_changed_between_initial_validation_and_hashing_are_rejected(self):
        changed = False

        def change_before_hash(path):
            nonlocal changed
            if Path(path) == self.source / 'validation-metadata.json' and not changed:
                changed = True
                (self.source / 'output/training-result.json').write_text('{"samples":101}')
                (self.source / 'config.json').write_text('{"samples":101}')
            return file_hash(path)

        with patch('coach_ml.snapshot.file_hash', side_effect=change_before_hash):
            with self.assertRaisesRegex(AuditError, 'changed before copying'):
                self.run_snapshot()
        self.assertFalse((self.output / 'snapshot-audit.json').exists())
        self.assertFalse((self.output / 'evaluation-audit.json').exists())
