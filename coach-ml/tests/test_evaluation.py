import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from coach_ml.audit import AuditError
from coach_ml.competencies import expected_response, project_request
from coach_ml.evaluation import evaluate, score_response
from test_project import scene


class EvaluationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'output').mkdir()
        self.metadata = []
        for index, skill in enumerate(['position-coverage', 'economy-snapshot']):
            request = project_request(scene(), skill)
            self.metadata.append({'row': index, 'matchId': '100', 'heroId': 41,
                'episodeId': 'death-41-110', 'competencyId': skill,
                'exampleId': '100/41/death-41-110/' + skill,
                'request': request, 'target': expected_response(request)})
        (self.root / 'validation-metadata.json').write_text(json.dumps(self.metadata))
        self.rows = {variant: [dict(row=m['row'], matchId=m['matchId'], heroId=m['heroId'],
            episodeId=m['episodeId'], exampleId=m['exampleId'], competencyId=m['competencyId'],
            variant=variant, text=json.dumps(m['target']), hitTokenLimit=False) for m in self.metadata]
            for variant in ['base', 'adapted']}
        self.save()

    def save(self):
        for variant, rows in self.rows.items():
            (self.root / 'output' / ('predictions-' + variant + '.jsonl')).write_text(
                ''.join(json.dumps(r) + '\n' for r in rows))

    def test_reports_each_competency_and_does_not_change_training_status(self):
        status = self.root / 'live-status.json'
        status.write_text('{"status":"training"}')
        result = evaluate(self.root)
        self.assertEqual(result['summary'][0]['exactPolicy'], 2)
        self.assertEqual(result['summary'][0]['byCompetency']['economy-snapshot']['cases'], 1)
        self.assertEqual(status.read_text(), '{"status":"training"}')
        self.assertFalse(result['tacticalImprovementValidated'])

    def test_wrong_competency_cannot_receive_factual_credit(self):
        target = self.metadata[0]['target']
        wrong = copy.deepcopy(target)
        wrong['competencyId'] = 'objective-records'
        result = score_response(json.dumps(wrong), target)
        self.assertFalse(result['valid'])
        self.assertFalse(result['observationCorrect'])

    def test_same_scene_predictions_swapped_between_competencies_are_rejected(self):
        self.rows['base'][0]['exampleId'] = self.rows['base'][1]['exampleId']
        self.save()
        with self.assertRaisesRegex(AuditError, 'competency identity'):
            evaluate(self.root)
        self.assertFalse((self.root / 'evaluation-audit.json').exists())

    def test_truncated_valid_json_does_not_receive_credit(self):
        self.rows['adapted'][0]['hitTokenLimit'] = True
        self.save()
        result = evaluate(self.root)['summary'][1]
        self.assertEqual(result['truncated'], 1)
        self.assertEqual(result['strictValid'], 1)
        self.assertEqual(result['exactPolicy'], 1)

    def test_fenced_json_is_counted_only_in_normalized_metrics(self):
        self.rows['base'][0]['text'] = '```json\n' + self.rows['base'][0]['text'] + '\n```'
        self.save()
        result = evaluate(self.root)['summary'][0]
        self.assertEqual(result['strictValid'], 1)
        self.assertEqual(result['normalizedValid'], 2)

    def test_incomplete_predictions_never_publish_partial_evaluation(self):
        self.rows['adapted'].pop()
        self.save()
        with self.assertRaisesRegex(AuditError, 'Incomplete'):
            evaluate(self.root)
        self.assertFalse((self.root / 'evaluation-audit.json').exists())

    def test_boolean_row_identity_and_missing_truncation_are_rejected(self):
        for mutate in [lambda r: r.update(row=False), lambda r: r.pop('hitTokenLimit')]:
            with self.subTest(mutate=mutate):
                original = copy.deepcopy(self.rows['base'][0])
                mutate(self.rows['base'][0])
                self.save()
                with self.assertRaises(AuditError):
                    evaluate(self.root)
                self.rows['base'][0] = original

    def test_existing_evaluation_is_preserved(self):
        result = evaluate(self.root)
        with self.assertRaisesRegex(AuditError, 'already exists'):
            evaluate(self.root)
        self.assertEqual(json.loads((self.root / 'evaluation-audit.json').read_text()), result)

    def test_concurrent_evaluation_lock_is_preserved(self):
        lock = self.root / '.evaluation.lock'
        lock.write_text('other-process')
        with self.assertRaises(FileExistsError):
            evaluate(self.root)
        self.assertEqual(lock.read_text(), 'other-process')
        self.assertFalse((self.root / 'evaluation-audit.json').exists())

    def test_equal_total_accuracy_does_not_hide_a_gain_and_a_regression(self):
        self.rows['base'][0]['text'] = '{}'
        self.rows['adapted'][1]['text'] = '{}'
        self.save()
        result = evaluate(self.root)
        metric = result['pairedComparison']['overall']['metrics']['strictObservationCorrect']
        self.assertEqual(metric, {'bothCorrect': 0, 'gained': 1, 'regressed': 1, 'bothIncorrect': 0})
        self.assertEqual(result['pairedComparison']['independentGames'], 1)
        self.assertEqual(result['pairedComparison']['byMatch']['100']['cases'], 2)

    def test_case_artifacts_preserve_identity_diagnosis_and_input_hashes_without_copying_text(self):
        self.rows['adapted'][1]['hitTokenLimit'] = True
        self.save()
        result = evaluate(self.root)
        cases = [json.loads(line) for line in (self.root / 'evaluation-cases.jsonl').read_text().splitlines()]
        self.assertEqual(cases[1]['exampleId'], self.metadata[1]['exampleId'])
        self.assertEqual(cases[1]['adapted']['failureReasons'], ['truncated'])
        self.assertNotIn('text', cases[1]['adapted'])
        self.assertEqual(len(result['sourceFiles']), 3)
        self.assertEqual(result['caseReport']['cases'], 2)

    def test_failed_evaluation_does_not_publish_cases_and_releases_lock(self):
        self.rows['adapted'].pop()
        self.save()
        with self.assertRaises(AuditError):
            evaluate(self.root)
        for name in ['evaluation-cases.jsonl', 'evaluation-cases.jsonl.tmp', '.evaluation.lock']:
            self.assertFalse((self.root / name).exists())

    def test_previous_partial_or_final_case_report_is_preserved(self):
        for name in ['evaluation-cases.jsonl', 'evaluation-cases.jsonl.tmp']:
            with self.subTest(name=name):
                artifact = self.root / name
                artifact.write_text('keep')
                with self.assertRaises((AuditError, FileExistsError)):
                    evaluate(self.root)
                self.assertEqual(artifact.read_text(), 'keep')
                artifact.unlink()

    def test_text_difference_is_not_reported_as_observation_failure(self):
        target = copy.deepcopy(self.metadata[0]['target'])
        target['exercise'] = 'Outra formulação provisória.'
        self.rows['adapted'][0]['text'] = json.dumps(target)
        self.save()
        evaluate(self.root)
        case = json.loads((self.root / 'evaluation-cases.jsonl').read_text().splitlines()[0])
        self.assertEqual(case['adapted']['failureReasons'], ['policy-text-difference'])

    def test_unsupported_legacy_target_is_rejected_instead_of_counting_false_failures(self):
        self.metadata[0].pop('competencyId')
        self.metadata[0]['target'] = {'schemaVersion': 3, 'reflections': []}
        (self.root / 'validation-metadata.json').write_text(json.dumps(self.metadata))
        with self.assertRaisesRegex(AuditError, 'Unsupported evaluation target'):
            evaluate(self.root)
        self.assertFalse((self.root / 'evaluation-audit.json').exists())
        self.assertFalse((self.root / 'evaluation-cases.jsonl').exists())

    def test_malformed_metadata_target_fails_without_partial_reports(self):
        self.metadata[0]['target'] = []
        (self.root / 'validation-metadata.json').write_text(json.dumps(self.metadata))
        with self.assertRaisesRegex(AuditError, 'Invalid evaluation metadata'):
            evaluate(self.root)
        self.assertFalse((self.root / '.evaluation.lock').exists())
        self.assertFalse((self.root / 'evaluation-cases.jsonl.tmp').exists())

    def test_factual_format_remains_supported_without_competency_prediction_fields(self):
        import test_audit
        fixture = test_audit.LearningAuditTests()
        fixture.hash = 'a' * 64
        _, meta = fixture.example()
        (self.root / 'validation-metadata.json').write_text(json.dumps([meta]))
        for variant in ['base', 'adapted']:
            self.rows[variant] = [{'row': 0, 'variant': variant, 'matchId': meta['matchId'],
                'episodeId': meta['episodeId'], 'text': json.dumps(meta['target']), 'hitTokenLimit': False}]
        self.save()
        result = evaluate(self.root)
        self.assertEqual(result['summary'][0]['exactPolicy'], 1)
        self.assertEqual(result['pairedComparison']['byCompetency']['factual-review']['cases'], 1)

    def test_input_changed_while_scoring_prevents_publication(self):
        import coach_ml.evaluation as module
        original = module._score

        def change_after_scoring(root, stream):
            result = original(root, stream)
            with (root / 'validation-metadata.json').open('a') as file:
                file.write(' ')
            return result

        with patch.object(module, '_score', side_effect=change_after_scoring):
            with self.assertRaisesRegex(AuditError, 'inputs changed'):
                evaluate(self.root)
        self.assertFalse((self.root / 'evaluation-audit.json').exists())
        self.assertFalse((self.root / 'evaluation-cases.jsonl').exists())
