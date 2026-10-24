import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from coach_ml.core.validation import AuditError
from coach_ml.tactical.content_diagnostics import CRITERIA, diagnose


def fixture():
    def row(rule, good):
        return {'rule': rule, **{key: good for key in CRITERIA}}
    return {'schemaVersion': 1, 'kind': 'tactical-content-agreement',
            'variants': {'base': {'cases': [row('R1', False), row('R1', True), row('R0', True), row('R2', False)]},
                         'adapted': {'cases': [row('R1', True), row('R1', False), row('R0', True), row('R2', False)]}}}


class ContentDiagnosticsTests(unittest.TestCase):
    def test_gain_and_regression_are_visible_even_with_zero_net_change(self):
        report = fixture()
        original = copy.deepcopy(report)
        result = diagnose(report)
        self.assertEqual(report, original)
        content = result['criteria']['contentCorrect']
        self.assertEqual(content['gainedRows'], [0])
        self.assertEqual(content['regressedRows'], [1])
        self.assertEqual((content['netChange'], content['stableCorrect'], content['stableIncorrect']), (0, 1, 1))
        self.assertEqual(result['byRule']['R1']['cases'], 2)
        self.assertFalse(result['tacticalQualityApproved'])
        self.assertNotIn('passed', result)

    def test_criteria_are_independent(self):
        report = fixture()
        report['variants']['adapted']['cases'][0]['numbersCorrect'] = False
        result = diagnose(report)
        self.assertEqual(result['criteria']['numbersCorrect']['gainedRows'], [])
        self.assertEqual(result['criteria']['ruleCorrect']['gainedRows'], [0])

    def test_misaligned_and_malformed_reports_fail_closed(self):
        mutations = [lambda r: r['variants']['adapted']['cases'].pop(),
                     lambda r: r['variants']['adapted']['cases'][0].update(rule='R2'),
                     lambda r: r['variants']['adapted']['cases'][0].update(valid=1),
                     lambda r: r.update(kind='other')]
        for mutate in mutations:
            report = fixture()
            mutate(report)
            with self.assertRaises(AuditError):
                diagnose(report)

    def test_malformed_variant_rule_and_boolean_schema_raise_domain_errors(self):
        mutations = [lambda r: r.update(schemaVersion=True),
                     lambda r: r['variants'].update(base=None),
                     lambda r: r['variants'].update(adapted=[]),
                     lambda r: r['variants']['base']['cases'][0].update(rule=[]),
                     lambda r: r['variants']['base']['cases'].__setitem__(0, None)]
        for mutation in mutations:
            report = fixture()
            mutation(report)
            with self.subTest(report=report), self.assertRaises(AuditError):
                diagnose(report)

    def test_cli_preserves_source_and_refuses_existing_output(self):
        from coach_ml.cli import main
        with tempfile.TemporaryDirectory() as folder:
            source, output = Path(folder) / 'f3.json', Path(folder) / 'diagnostics.json'
            source.write_text(json.dumps(fixture()))
            original = source.read_bytes()
            args = ['tactical-content-diagnostics', '--report', str(source), '--output', str(output)]
            with patch('sys.stdout'), patch('sys.stderr'):
                self.assertEqual(main(args), 0)
                result = output.read_bytes()
                self.assertEqual(main(args), 2)
            self.assertEqual(source.read_bytes(), original)
            self.assertEqual(output.read_bytes(), result)
            self.assertEqual(len(json.loads(result)['sourceReportSha256']), 64)

    def test_new_cli_modules_remain_tensor_free(self):
        code = ('import sys; import coach_ml.runtime_qwen35.audit; '
                'import coach_ml.tactical.content_diagnostics; '
                'assert "torch" not in sys.modules; assert "transformers" not in sys.modules')
        subprocess.run([sys.executable, '-B', '-c', code], check=True, capture_output=True)
