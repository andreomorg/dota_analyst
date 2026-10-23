import json
from pathlib import Path
import tempfile
import unittest

from coach_ml.core.validation import AuditError
from coach_ml.experiments.preparation import load_config


class Qwen17BConfigTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.template = json.loads((Path(__file__).parents[1] /
            'configs/qwen3-1.7b.rtx3080.example.json').read_text())

    def load(self, config):
        (self.root / 'config.json').write_text(json.dumps(config))
        return load_config(self.root)

    def test_placeholder_requires_claude_to_supply_verified_commit(self):
        with self.assertRaises(AuditError): self.load(self.template)
        for revision in ('a' * 40, '0123456789abcdef' * 2 + '01234567'):
            self.assertEqual(self.load({**self.template, 'revision': revision})['revision'], revision)
        for revision in ('main', 'a' * 39, 'a' * 41, 'z' * 40, '', None):
            with self.assertRaises(AuditError): self.load({**self.template, 'revision': revision})

    def test_explicit_3080_profile_limits_memory_settings(self):
        config = {**self.template, 'revision': 'a' * 40}
        self.assertEqual(self.load(config)['model'], 'Qwen/Qwen3-1.7B')
        for change in ({'microBatch': 2}, {'inferenceBatch': 2}, {'gradientCheckpointing': False},
                       {'hardwareProfile': None}, {'memoryBudgetGiB': 12}, {'maxTrainingTokens': 4096}):
            with self.subTest(change=change), self.assertRaises(AuditError): self.load(config | change)

    def test_06b_existing_configuration_needs_no_hardware_profile(self):
        config = json.loads((Path(__file__).parents[1] / 'configs/lora.example.json').read_text())
        self.assertEqual(self.load(config), config)
