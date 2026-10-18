import copy
import tempfile
from pathlib import Path
import unittest

from coach_ml.competencies import COMPETENCIES, expected_response, project_request
from coach_ml.curriculum import build
from test_project import scene


class ContextualPolicyTests(unittest.TestCase):
    def request(self, skill):
        return project_request(scene(), skill, policy_version=2)

    def test_version_one_remains_default_and_input_is_not_mutated(self):
        for skill in COMPETENCIES:
            request = project_request(scene(), skill)
            before = copy.deepcopy(request)
            response = expected_response(request)
            explicit = expected_response(request | {'reviewPolicyVersion': 1})
            self.assertEqual(response, explicit)
            self.assertNotIn('reviewPolicyVersion', request)
            self.assertEqual(before, request)
            contextual = expected_response(request | {'reviewPolicyVersion': 2})
            self.assertEqual(response['observation'], contextual['observation'])
            self.assertEqual(response['evidenceIds'], contextual['evidenceIds'])
            self.assertNotEqual(response['exercise'], contextual['exercise'])

    def test_invalid_policy_versions_are_rejected_before_writing(self):
        for version in (True, 0, 3, '2', None):
            with self.subTest(version=version):
                with self.assertRaises(ValueError):
                    project_request(scene(), 'economy-snapshot', policy_version=version)
                with self.assertRaises(ValueError):
                    expected_response(self.request('economy-snapshot') | {'reviewPolicyVersion': version})
                with tempfile.TemporaryDirectory() as temp:
                    out = Path(temp) / 'output'
                    with self.assertRaises(ValueError):
                        build(Path(temp) / 'missing', out, policy_version=version)
                    self.assertFalse(out.exists())

    def test_missing_economy_does_not_become_zero_or_farm_prescription(self):
        request = self.request('economy-snapshot')
        request['evidence']['economy'] = None
        response = expected_response(request)
        self.assertIsNone(response['observation']['lastHits'])
        self.assertIn('indisponível', response['exercise'])
        self.assertNotIn('fonte de farm', response['exercise'])

    def test_economy_tasks_change_with_role_and_snapshot_age(self):
        request = self.request('economy-snapshot')
        core = expected_response(request)['exercise']
        request['role']['position'] = 5
        support = expected_response(request)['exercise']
        self.assertNotEqual(core, support)
        self.assertIn('tarefa de suporte', support)
        request['evidence']['economy']['sampleSeconds'] = 0
        stale = expected_response(request)['exercise']
        self.assertNotEqual(stale, support)
        self.assertIn('idade 100s', stale)
        self.assertNotIn('fonte de farm', stale)

    def test_opening_support_does_not_request_negative_time(self):
        request = self.request('support-change')
        request['evidence']['event'].update(anchorSeconds=0, eventSeconds=10)
        request['evidence']['support'].update(expectedPairedSeconds=0, observedPairedSeconds=0,
            earlier=None, atAnchor=None, allyHeroId=None, change='unavailable')
        response = expected_response(request)
        self.assertNotIn('-10s', response['exercise'])
        self.assertIn('indisponível', response['exercise'])

    def test_support_direction_changes_question_without_changing_identity(self):
        request = self.request('support-change')
        increased = expected_response(request)
        request['evidence']['support']['atAnchor']['distance'] = 50
        request['evidence']['support']['change'] = 'recorded-separation-decreased'
        decreased = expected_response(request)
        self.assertNotEqual(increased['exercise'], decreased['exercise'])
        self.assertEqual(increased['observation']['allyHeroId'], decreased['observation']['allyHeroId'])
        self.assertIn('lacunas', increased['exercise'])

    def test_history_unknown_empty_and_recorded_remain_distinct(self):
        for skill, field in [('purchase-records', 'recentPurchases'),
                             ('ability-records', 'recentAbilityUpgrades'),
                             ('objective-records', 'recentObjectives')]:
            request = self.request(skill)
            recorded = expected_response(request)
            self.assertIn('registro mais recente', recorded['exercise'])
            request['evidence'][field] = []
            empty = expected_response(request)
            request['evidence'][field] = None
            missing = expected_response(request)
            self.assertEqual(3, len({r['exercise'] for r in (recorded, empty, missing)}))
            self.assertIsNone(missing['observation']['count'])
            self.assertEqual(0, empty['observation']['count'])

    def test_future_data_still_rejected_under_new_policy(self):
        request = self.request('economy-snapshot')
        request['evidence']['economy']['sampleSeconds'] = 101
        with self.assertRaises(ValueError):
            expected_response(request)

    def test_position_alignment_does_not_invent_coordinates(self):
        request = self.request('position-coverage')
        exercise = expected_response(request)['exercise']
        self.assertIn('amostras precisaram de alinhamento', exercise)
        self.assertNotIn('distância registrada', exercise)
