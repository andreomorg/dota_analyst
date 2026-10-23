import copy
import json
import unittest
from contextlib import redirect_stdout, redirect_stderr
import io
from pathlib import Path
import tempfile

from coach_ml.tactical.content_evaluation import score_content, summarize, numbers
from coach_ml.tactical.policy_v2 import target_for
from coach_ml.tactical.situated import project_situated_scene
from test_tactical_policy_v2 import CASES, context


def case(index):
    request = project_situated_scene(context(CASES[index - 1][0] if index else {}), 0)
    rule, target = target_for(request)
    return request, rule, target


class ContentEvaluationTests(unittest.TestCase):
    def test_cli_exits_successfully_and_preserves_existing_report(self):
        from coach_ml.cli import main
        request, rule, target = case(1)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'output').mkdir()
            identity = {'row': 0, 'matchId': '1', 'heroId': 1, 'episodeId': 'scene', 'exampleId': 'example'}
            meta = {**identity, 'request': request, 'target': target, 'policyRule': rule}
            (root / 'validation-metadata.json').write_text(json.dumps([meta]))
            for variant in ('base', 'adapted'):
                prediction = {**identity, 'variant': variant, 'text': json.dumps(target), 'hitTokenLimit': False}
                (root / 'output' / f'predictions-{variant}.jsonl').write_text(json.dumps(prediction) + '\n')
            report = root / 'report.json'
            args = ['tactical-content-evaluate', '--experiment', str(root), '--output', str(report)]
            with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                self.assertEqual(main(args), 0)
                original = report.read_bytes()
                self.assertEqual(main(args), 2)
            self.assertEqual(report.read_bytes(), original)
            self.assertTrue(json.loads(original)['variants']['adapted']['summary']['passed'])

    def test_claude_v5_regression_32_of_188_with_same_rule_distribution(self):
        successes = [5, 0, 0, 9, 0, 0, 4, 14, 0]
        rows = []
        for index, passed in enumerate(successes):
            request, rule, target = case(index)
            for offset in range(28 if index == 0 else 20):
                candidate = copy.deepcopy(target)
                if offset >= passed:
                    candidate = copy.deepcopy(case(7)[2]) if index == 0 else candidate
                    if index:
                        candidate['alternatives'][0]['action'] = 'Outra ação'
                rows.append(score_content(request, target, json.dumps(candidate), expected_rule=rule))
        report = summarize(rows)
        self.assertEqual((report['contentCorrect'], report['cases']), (32, 188))
        self.assertEqual([report['byRule'][f'R{i}']['contentCorrect'] for i in range(9)], successes)
        self.assertFalse(report['passed'])

    def test_rule_actions_and_numbers_are_independent(self):
        request, _, target = case(1)
        candidate = copy.deepcopy(target)
        candidate['alternatives'][0]['action'] = 'Esperar'
        value = score_content(request, target, json.dumps(candidate))
        self.assertTrue(value['ruleCorrect'])
        self.assertTrue(value['numbersCorrect'])
        self.assertFalse(value['actionsCorrect'])
        candidate = copy.deepcopy(target)
        candidate['opportunity'] = candidate['opportunity'].replace('4 inimigos', '9999 inimigos')
        value = score_content(request, target, json.dumps(candidate))
        self.assertTrue(value['ruleCorrect'])
        self.assertFalse(value['numbersCorrect'])
        self.assertIn('9999', value['inventedNumbers'])

    def test_wrong_but_present_scene_number_still_fails_target_comparison(self):
        request, _, target = case(1)
        candidate = copy.deepcopy(target)
        candidate['opportunity'] = candidate['opportunity'].replace('4 inimigos', '2 inimigos')
        value = score_content(request, target, json.dumps(candidate))
        self.assertEqual(value['inventedNumbers'], [])
        self.assertFalse(value['numbersCorrect'])

    def test_r0_invention_veto_and_invalid_json(self):
        request, _, target = case(0)
        candidate = copy.deepcopy(target)
        candidate['opportunity'] += ' Valor 99999.'
        value = score_content(request, target, json.dumps(candidate))
        self.assertTrue(value['ruleCorrect'])
        self.assertEqual(summarize([value])['r0InventedNumberCases'], 1)
        self.assertFalse(summarize([value])['passed'])
        self.assertFalse(score_content(request, target, '{}')['valid'])
        self.assertFalse(score_content(request, target, json.dumps(target), True)['valid'])

    def test_success_gate_denominator_and_numeric_normalization(self):
        request, _, target = case(2)
        value = score_content(request, target, json.dumps(target))
        self.assertTrue(summarize([value])['passed'])
        self.assertEqual(numbers('R1–R8 v5.2 -12 23% 0,5'), [-12, 23, .5])
        self.assertFalse(summarize([score_content(request, target, 'no json')])['passed'])
