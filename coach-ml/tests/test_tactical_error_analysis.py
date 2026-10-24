import copy
import json
from pathlib import Path
import tempfile
import unittest

from coach_ml.core.validation import AuditError
from coach_ml.tactical.error_analysis import explain, analyze
from coach_ml.tactical.content_evaluation import score_content
from test_tactical_content_evaluation import case


class TacticalErrorAnalysisTests(unittest.TestCase):
    def test_independent_numeric_and_action_errors_preserve_f3(self):
        request, rule, target = case(1)
        candidate = copy.deepcopy(target)
        candidate['opportunity'] = candidate['opportunity'].replace('4 inimigos', '9999 inimigos')
        candidate['alternatives'][1]['action'] = 'Esperar'
        text = json.dumps(candidate)
        result = explain(request, target, text)
        self.assertEqual(result['score'], score_content(request, target, text))
        self.assertTrue({'wrong-actions', 'missing-numbers', 'extra-numbers', 'invented-numbers'} <= set(result['categories']))
        self.assertEqual(result['actionDifferences'][0]['alternative'], 'B')
        self.assertIn('4', result['numbers']['missing'])

    def test_invalid_truncated_wrong_and_indeterminate_rules(self):
        request, _, target = case(1)
        self.assertEqual(explain(request, target, '{}')['categories'], ['invalid-contract'])
        self.assertEqual(explain(request, target, 'not json')['categories'], ['invalid-json'])
        self.assertEqual(explain(request, target, '{}', True)['categories'], ['truncated'])
        self.assertIn('wrong-rule', explain(request, target, json.dumps(case(2)[2]))['categories'])
        candidate = copy.deepcopy(target)
        candidate['opportunity'] = 'Sem classificação.'
        self.assertIn('rule-indeterminate', explain(request, target, json.dumps(candidate))['categories'])

    def test_repeated_signed_decimal_and_percent_values_are_not_bound_to_fields(self):
        request, _, target = case(1)
        target = copy.deepcopy(target)
        target['opportunity'] += ' -12 0,5 23% 23%'
        candidate = copy.deepcopy(target)
        candidate['opportunity'] = candidate['opportunity'].replace('23% 23%', '23%')
        request = copy.deepcopy(request)
        request['facts'].append({'id': 'signals', 'value': [{'numbers': {'a': 23, 'b': 23}}]})
        result = explain(request, target, json.dumps(candidate))
        self.assertEqual(result['numbers']['missing'], ['23'])
        self.assertIn('-12', result['numbers']['expected'])
        self.assertIn('0.5', result['numbers']['expected'])
        self.assertEqual(result['fieldAttribution'], 'ambiguous-free-prose')

    def test_file_analysis_reuses_identity_checks_and_exact_summary(self):
        from coach_ml.tactical.content_evaluation import evaluate_content
        request, rule, target = case(1)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'output').mkdir()
            identity = {'row': 0, 'matchId': '1', 'heroId': 1, 'episodeId': 'scene', 'exampleId': 'id'}
            (root / 'validation-metadata.json').write_text(json.dumps([
                {**identity, 'request': request, 'target': target, 'policyRule': rule}]))
            for variant in ('base', 'adapted'):
                (root / 'output' / f'predictions-{variant}.jsonl').write_text(json.dumps(
                    {**identity, 'variant': variant, 'text': json.dumps(target), 'hitTokenLimit': False}) + '\n')
            result = analyze(root)
            self.assertEqual(result['variants']['base']['summary'], evaluate_content(root)['variants']['base']['summary'])
            self.assertEqual(result['variants']['adapted']['categories'], {})
            self.assertEqual(result['variants']['adapted']['ruleConfusion'], {rule: {rule: 1}})
            path = root / 'output/predictions-adapted.jsonl'
            prediction = json.loads(path.read_text())
            prediction['text'] = json.dumps(case(2)[2])
            path.write_text(json.dumps(prediction) + '\n')
            self.assertEqual(analyze(root)['variants']['adapted']['ruleConfusion'], {rule: {'R2': 1}})
            prediction['hitTokenLimit'] = True
            path.write_text(json.dumps(prediction) + '\n')
            self.assertEqual(analyze(root)['variants']['adapted']['ruleConfusion'], {rule: {'truncated': 1}})
            prediction.update(text='not json', hitTokenLimit=False)
            path.write_text(json.dumps(prediction) + '\n')
            self.assertEqual(analyze(root)['variants']['adapted']['ruleConfusion'], {rule: {'invalid': 1}})
            path = root / 'output/predictions-base.jsonl'
            path.write_text(path.read_text().replace('"heroId": 1', '"heroId": 2'))
            with self.assertRaises(AuditError): analyze(root)
