import json
from pathlib import Path
import tempfile
import unittest

from coach_ml.audit import AuditError
from coach_ml.tokenization import audit_tokens


class CharacterTokenizer:
    eos_token = '!'

    def apply_chat_template(self, messages, **kwargs):
        if kwargs.get('enable_thinking') is not False or kwargs.get('add_generation_prompt') is not True:
            raise AssertionError('Must use the same non-thinking template as training')
        return 'prompt'

    def __call__(self, text, add_special_tokens):
        if add_special_tokens:
            raise AssertionError('Special tokens would be counted twice')
        return {'input_ids': list(range(len(text)))}


class TokenizationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.snapshot = self.root / ('a' * 40)
        self.snapshot.mkdir()
        self.data = self.root / 'data.jsonl'
        self.messages = [{'role': 'system', 'content': 'system'}, {'role': 'user', 'content': 'user'},
                         {'role': 'assistant', 'content': 'abc'}]
        self.save()

    def save(self):
        self.data.write_text(json.dumps({'messages': self.messages}) + '\n')

    def test_exact_budget_includes_eos_and_does_not_truncate(self):
        report = audit_tokens([self.data], self.snapshot, 10, 4, CharacterTokenizer())
        self.assertTrue(report['tokenBudgetsPassed'])
        self.assertEqual(report['datasets'][0]['maxTargetTokens'], 4)
        self.assertFalse(report['examplesTruncated'])
        self.assertFalse(report['modelWeightsLoaded'])

    def test_training_and_generation_overflow_are_reported_separately(self):
        for training, generation, expected in [(9, 4, (1, 0)), (10, 3, (0, 1))]:
            with self.subTest(training=training, generation=generation):
                report = audit_tokens([self.data], self.snapshot, training, generation, CharacterTokenizer())
                row = report['datasets'][0]
                self.assertEqual((row['trainingOverflowExamples'], row['generationOverflowExamples']), expected)
                self.assertFalse(report['tokenBudgetsPassed'])

    def test_wrong_roles_are_rejected_before_tokenization(self):
        self.messages[2]['role'] = 'user'
        self.save()
        with self.assertRaisesRegex(AuditError, 'messages'):
            audit_tokens([self.data], self.snapshot, 10, 4, CharacterTokenizer())

    def test_unpinned_snapshot_and_boolean_budget_are_rejected(self):
        for snapshot, budget in [(self.root, 10), (self.snapshot, True)]:
            with self.subTest(snapshot=snapshot, budget=budget), self.assertRaises(AuditError):
                audit_tokens([self.data], snapshot, budget, 4, CharacterTokenizer())
