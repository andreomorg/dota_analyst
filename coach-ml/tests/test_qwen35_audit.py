import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from coach_ml.core.validation import AuditError
from coach_ml.runtime_qwen35.audit import audit
from test_qwen35_runtime import config
from test_qwen35_training import Tokenizer


class Qwen35AuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.cfg = config()
        self.snapshot = self.root / self.cfg['revision']
        self.snapshot.mkdir()
        for name in ('tokenizer.json', 'tokenizer_config.json', 'chat_template.jinja'):
            (self.snapshot / name).write_text('synthetic')
        self.messages = [{'role': role, 'content': 'value'} for role in ('system', 'user', 'assistant')]
        self.write()

    def write(self):
        (self.root / 'config.json').write_text(json.dumps(self.cfg))
        for split in ('development', 'validation'):
            (self.root / (split + '.jsonl')).write_text(json.dumps({'messages': self.messages}) + '\n')
            (self.root / (split + '-metadata.json')).write_text(json.dumps([{'row': 0, 'policyRule': 'R1'}]))

    def test_counts_shared_encoding_and_records_hashes_without_changing_inputs(self):
        result = audit(self.root, self.snapshot, Tokenizer())
        self.assertTrue(result['passed'])
        self.assertFalse(result['modelWeightsLoaded'])
        report = result['datasets']['development']
        self.assertEqual((report['maxTrainingTokens'], report['maxTargetTokens']), (4, 2))
        self.assertEqual(report['byRule'], {'R1': 1})
        self.assertIn('R0', report['missingRules'])
        self.assertEqual(len(result['sourceFiles']), 8)

    def test_collects_overflows_without_truncating_or_stopping_at_first_row(self):
        self.cfg.update(maxTrainingTokens=3, maxNewTokens=1, samples=2)
        self.write()
        path = self.root / 'development.jsonl'
        path.write_text(path.read_text() * 2)
        (self.root / 'development-metadata.json').write_text(json.dumps([
            {'row': 0, 'policyRule': 'R1'}, {'row': 1, 'policyRule': 'R2'}]))
        result = audit(self.root, self.snapshot, Tokenizer())
        self.assertFalse(result['passed'])
        report = result['datasets']['development']
        self.assertEqual(report['generationOverflowExamples'], 2)
        self.assertEqual(report['trainingOverflowExamples'], 2)
        self.assertEqual([row['row'] for row in report['failures']], [0, 1])

    def test_invalid_controls_are_reported_even_when_no_row_encodes(self):
        self.messages[1]['content'] = '<end>'
        self.write()
        result = audit(self.root, self.snapshot, Tokenizer())
        report = result['datasets']['development']
        self.assertFalse(result['passed'])
        self.assertEqual(report['encodedRows'], 0)
        self.assertIsNone(report['maxTrainingTokens'])
        self.assertIn('control token', report['failures'][0]['error'])

    def test_misaligned_metadata_counts_and_unknown_rules_fail_closed(self):
        path = self.root / 'development-metadata.json'
        for meta in ([], [{'row': 1, 'policyRule': 'R1'}], [{'row': 0, 'policyRule': 'R99'}]):
            path.write_text(json.dumps(meta))
            with self.subTest(meta=meta), self.assertRaises(AuditError):
                audit(self.root, self.snapshot, Tokenizer())
        self.cfg['samples'] = 2
        self.write()
        with self.assertRaisesRegex(AuditError, 'sample count'):
            audit(self.root, self.snapshot, Tokenizer())

    def test_changed_inputs_and_existing_output_are_rejected(self):
        tokenizer = Tokenizer()
        original = tokenizer.apply_chat_template
        def mutate(*args, **kwargs):
            (self.snapshot / 'tokenizer.json').write_text('changed')
            return original(*args, **kwargs)
        tokenizer.apply_chat_template = mutate
        with self.assertRaisesRegex(AuditError, 'inputs changed'):
            audit(self.root, self.snapshot, tokenizer)
        from coach_ml.cli import main
        output = self.root / 'report.json'
        output.write_text('preserve')
        with patch('sys.stderr'):
            self.assertEqual(main(['qwen35-audit', '--experiment', str(self.root), '--snapshot',
                                   str(self.snapshot), '--output', str(output)]), 2)
        self.assertEqual(output.read_text(), 'preserve')

    def test_config_swap_between_validation_and_hashing_is_rejected(self):
        from coach_ml.experiments.preparation import load_config
        def swap(root):
            cfg = load_config(root)
            replacement = {**cfg, 'maxNewTokens': 1}
            (root / 'config.json').write_text(json.dumps(replacement))
            return cfg
        with patch('coach_ml.runtime_qwen35.audit.load_config', side_effect=swap):
            with self.assertRaisesRegex(AuditError, 'config changed'):
                audit(self.root, self.snapshot, Tokenizer())
