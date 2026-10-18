import copy
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from contextlib import redirect_stderr, redirect_stdout

from coach_ml import cli
from coach_ml.audit import AuditError, audit_sft, iter_json_array
from coach_ml.competencies import COMPETENCIES, expected_response, project_request
from coach_ml.curriculum import build, file_hash
from coach_ml.datasets import audit_experiment, main as dataset_main
from coach_ml.evaluation import score_response
from coach_ml.training import inspect_experiment, load_config, main as training_main


def scene(match='100', split='train'):
    return {'id': f'{match}/41/death-41-110', 'split': split, 'sourceHash': 'a' * 64,
            'heroId': 41, 'role': {'position': 1 if match == '100' else 2, 'isInferred': False},
            'retrospectiveOutcome': {'id': 'death-41-110', 'kind': 0, 'eventSeconds': 110},
            'input': {'atOrBeforeAnchor': {'anchorSeconds': 100,
                'positions': [{'heroId': 41, 'team': 0, 'sampleSeconds': 100},
                              {'heroId': 100, 'team': 0, 'sampleSeconds': 99}], 'missingHeroPositions': 8,
                'economy': {'sampleSeconds': 60, 'lastHits': 5, 'netWorth': 1000},
                'recentPurchases': [{'heroId': 41, 'seconds': 95, 'itemKey': 'magic_wand'}],
                'recentAbilityUpgrades': [{'heroId': 41, 'seconds': 92, 'abilityName': 'time_walk', 'level': 2}],
                'recentObjectives': [{'kind': 0, 'seconds': 80, 'takenBy': 0}]}},
            'supportProvenance': {'allyHeroId': 100, 'change': 1,
                'earlier': {'targetSeconds': 90, 'viewerSampleSeconds': 90, 'allySampleSeconds': 89, 'distance': 100},
                'atAnchor': {'targetSeconds': 100, 'viewerSampleSeconds': 100, 'allySampleSeconds': 99, 'distance': 200},
                'expectedPairedSeconds': 11, 'observedPairedSeconds': 6, 'unavailableReason': None}}


class CompetencyTests(unittest.TestCase):
    def request(self, skill):
        return project_request(scene(), skill)

    def test_serializing_sorted_json_keys_preserves_reference_order_and_response(self):
        for skill in COMPETENCIES:
            request = self.request(skill)
            reparsed = json.loads(json.dumps(request, sort_keys=True))
            self.assertEqual(expected_response(request), expected_response(reparsed))

    def test_position_coverage_preserves_mixed_sample_times(self):
        response = expected_response(self.request('position-coverage'))
        self.assertEqual(response['observation'], {'viewerObserved': True, 'observedHeroes': 2,
            'missingHeroPositions': 8, 'observedAllies': 1, 'sampleSynchronization': 'mixed-recorded-seconds'})

    def test_missing_viewer_differs_from_no_observed_ally(self):
        request = self.request('position-coverage')
        request['evidence']['positions'] = {'samples': [{'heroId': 100, 'team': 0, 'sampleSeconds': 100}], 'missingHeroPositions': 9}
        missing = expected_response(request)
        self.assertIsNone(missing['observation']['observedAllies'])
        request['evidence']['positions']['samples'][0]['heroId'] = 41
        observed = expected_response(request)
        self.assertEqual(observed['observation']['observedAllies'], 0)
        self.assertNotEqual(missing['exercise'], observed['exercise'])

    def test_future_and_duplicate_positions_are_rejected(self):
        for modification in ['future', 'duplicate']:
            with self.subTest(modification=modification):
                request = self.request('position-coverage')
                request['evidence']['positions']['samples'][1][
                    'sampleSeconds' if modification == 'future' else 'heroId'] = 101 if modification == 'future' else 41
                with self.assertRaises(ValueError):
                    expected_response(request)

    def test_support_change_comes_from_both_distances_and_reports_partial_coverage(self):
        response = expected_response(self.request('support-change'))
        self.assertEqual(response['observation'], {'change': 'recorded-separation-increased',
            'allyHeroId': 100, 'earlierDistance': 100, 'anchorDistance': 200, 'pairedCoverage': 'incomplete-paired-seconds'})

    def test_inconsistent_support_change_is_rejected(self):
        request = self.request('support-change')
        request['evidence']['support']['change'] = 'recorded-separation-decreased'
        with self.assertRaisesRegex(ValueError, 'contradicts'):
            expected_response(request)

    def test_unknown_support_change_keeps_known_anchor_distance(self):
        request = self.request('support-change')
        request['evidence']['support'].update(earlier=None, change='unavailable')
        observation = expected_response(request)['observation']
        self.assertIsNone(observation['earlierDistance'])
        self.assertEqual(observation['anchorDistance'], 200)

    def test_opening_scene_does_not_request_positions_before_game_start(self):
        request = self.request('support-change')
        request['evidence']['event'].update(anchorSeconds=5, eventSeconds=15)
        request['evidence']['support'].update(earlier=None, change='unavailable',
            expectedPairedSeconds=0, observedPairedSeconds=0,
            atAnchor={'targetSeconds': 5, 'viewerSampleSeconds': 5, 'allySampleSeconds': 5, 'distance': 200})
        result = expected_response(request)
        self.assertEqual(result['observation']['pairedCoverage'], 'no-window')
        self.assertNotIn('Recupere', result['exercise'])
        self.assertIn('início', result['exercise'])

    def test_recorded_talent_is_not_presented_as_a_castable_ability(self):
        request = self.request('ability-records')
        request['evidence']['recentAbilityUpgrades'][0]['abilityName'] = 'special_bonus_unique_nyx'
        result = expected_response(request)
        self.assertEqual(result['observation']['latestEvent']['abilityName'], 'special_bonus_unique_nyx')
        self.assertIn('ativa, passiva ou talento', result['exercise'])

    def test_position_coverage_without_coordinates_does_not_claim_a_distance(self):
        result = expected_response(self.request('position-coverage'))
        self.assertNotIn('distância', result['exercise'])

    def test_economy_age_and_support_role_change_the_review_question(self):
        request = self.request('economy-snapshot')
        core = expected_response(request)
        self.assertEqual(core['observation']['sampleAgeSeconds'], 40)
        request['role']['position'] = 5
        support = expected_response(request)
        self.assertNotEqual(core['exercise'], support['exercise'])
        self.assertIn('suporte', support['exercise'])

    def test_missing_economy_is_not_zero_and_future_snapshot_is_rejected(self):
        request = self.request('economy-snapshot')
        request['evidence']['economy'] = None
        self.assertIsNone(expected_response(request)['observation']['netWorth'])
        request['evidence']['economy'] = {'sampleSeconds': 101, 'lastHits': 5, 'netWorth': 1000}
        with self.assertRaisesRegex(ValueError, 'Future'):
            expected_response(request)

    def test_history_unknown_empty_and_recorded_have_distinct_exercises(self):
        for skill, key in [('purchase-records', 'recentPurchases'), ('ability-records', 'recentAbilityUpgrades'),
                          ('objective-records', 'recentObjectives')]:
            with self.subTest(skill=skill):
                request = self.request(skill)
                recorded = expected_response(request)
                request['evidence'][key] = []
                empty = expected_response(request)
                request['evidence'][key] = None
                missing = expected_response(request)
                self.assertEqual(recorded['observation']['count'], 1)
                self.assertEqual(empty['observation']['count'], 0)
                self.assertIsNone(missing['observation']['count'])
                self.assertEqual(len({r['exercise'] for r in [recorded, empty, missing]}), 3)

    def test_future_history_and_another_hero_are_rejected(self):
        for skill, key in [('purchase-records', 'recentPurchases'), ('ability-records', 'recentAbilityUpgrades')]:
            request = self.request(skill)
            request['evidence'][key][0]['heroId'] = 100
            with self.assertRaisesRegex(ValueError, 'another hero'):
                expected_response(request)
            request['evidence'][key][0]['heroId'] = 41
            request['evidence'][key][0]['seconds'] = 101
            with self.assertRaisesRegex(ValueError, 'Future'):
                expected_response(request)


class ProjectIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'source'
        (self.source / 'model-corpus').mkdir(parents=True)
        self.manifest = self.source / 'manifest.json'
        self.manifest.write_text(json.dumps({'matches': [
            {'matchId': match, 'split': split, 'sourceHash': 'a' * 64}
            for match, split in [('100', 'train'), ('200', 'validation-new'), ('300', 'reserved-test')]]}))
        hashes = {}
        for split, rows in [('train-known', []), ('train', [scene()]),
                            ('validation-new', [scene('200', 'validation-new')])]:
            name = f'model-corpus/{split}.scenes.jsonl'
            file = self.source / name
            file.write_text(''.join(json.dumps(row) + '\n' for row in rows))
            hashes[name] = {'sha256': file_hash(file)}
        (self.source / 'audit.json').write_text(json.dumps({'corpusFiles': hashes}))
        self.output = self.root / 'candidate'

    def build(self):
        return build(self.source, self.output, cap=1)

    def test_curriculum_builds_six_distinct_tasks_without_opening_reserved_test(self):
        result = self.build()
        self.assertEqual(result['examples'], {'train': 6, 'validation': 6})
        self.assertEqual(result['matches'], {'train': 1, 'validation': 1})
        self.assertFalse(result['testOpened'])
        report = audit_sft(self.output / 'development.jsonl', self.output / 'development-metadata.json',
                           self.manifest, 'train', 'competency-review-v1')
        self.assertEqual(set(report['competencyDiversity']), set(COMPETENCIES))
        self.assertEqual(report['uniqueExercises'], 6)
        self.assertFalse(report['trainingEligibilityGranted'])

    def test_builder_never_overwrites_an_existing_destination(self):
        self.build()
        with self.assertRaises(FileExistsError):
            self.build()

    def test_policy_two_builder_outputs_auditable_contextual_targets(self):
        result = build(self.source, self.output, cap=1, policy_version=2)
        self.assertEqual(2, result['reviewPolicyVersion'])
        report = audit_experiment(self.output, self.manifest, 'competency-review-v1')
        self.assertFalse(report['trainingEligibilityGranted'])
        meta = list(iter_json_array(self.output / 'development-metadata.json'))
        self.assertEqual(6, len(meta))
        self.assertTrue(all(row['request']['reviewPolicyVersion'] == 2 for row in meta))
        self.assertTrue(all(row['target']['exercise'].startswith('Na âncora 100s') for row in meta))

    def test_declared_source_file_hash_is_checked_before_building(self):
        with (self.source / 'model-corpus/train.scenes.jsonl').open('a') as stream:
            stream.write('\n')
        with self.assertRaisesRegex(AuditError, 'Source corpus changed'):
            self.build()
        self.assertFalse(self.output.exists())

    def test_reusing_a_scene_for_the_same_competency_is_rejected(self):
        self.build()
        data = self.output / 'development.jsonl'
        metadata = self.output / 'development-metadata.json'
        rows = data.read_text().splitlines()
        meta = list(iter_json_array(metadata))
        rows[1], meta[1] = rows[0], copy.deepcopy(meta[0])
        meta[1]['row'] = 1
        data.write_text('\n'.join(rows) + '\n')
        metadata.write_text(json.dumps(meta))
        with self.assertRaisesRegex(AuditError, 'Duplicate source scene'):
            audit_sft(data, metadata, self.manifest, 'train', 'competency-review-v1')

    def configure(self):
        result = self.build()
        config = json.loads((Path(__file__).parents[1] / 'configs/lora.example.json').read_text())
        config.update(samples=result['examples']['train'], validationSamples=result['examples']['validation'])
        (self.output / 'config.json').write_text(json.dumps(config))
        (self.output / 'experimental-authorization.json').write_text(json.dumps({'scope': 'Unit test; no optimization'}))
        cache = self.root / 'cache'
        (cache / 'hub/models--Qwen--Qwen3-0.6B/snapshots' / config['revision']).mkdir(parents=True)
        return cache

    def test_inspect_runs_without_importing_the_ml_stack(self):
        import sys
        cache = self.configure()
        report = inspect_experiment(self.output, cache, self.manifest)
        self.assertFalse(report['gpuInitialized'])
        self.assertEqual(report['data']['train']['rows'], 6)
        self.assertNotIn('torch', sys.modules)

    def collide_validation_inputs(self):
        data = self.output / 'validation.jsonl'
        metadata_file = self.output / 'validation-metadata.json'
        metadata = list(iter_json_array(metadata_file))
        rows = [json.loads(line) for line in data.read_text().splitlines()]
        for row, meta in zip(rows, metadata):
            meta['request']['role']['position'] = 1
            meta['target'] = expected_response(meta['request'])
            row['messages'][1]['content'] = json.dumps(meta['request'])
            row['messages'][2]['content'] = json.dumps(meta['target'])
        data.write_text(''.join(json.dumps(row) + '\n' for row in rows))
        metadata_file.write_text(json.dumps(metadata))

    def test_joint_audit_detects_identical_inputs_from_different_matches(self):
        self.build()
        self.collide_validation_inputs()
        report = audit_experiment(self.output, self.manifest, 'competency-review-v1')
        self.assertEqual(report['sharedMatchesTrainValidation'], 0)
        self.assertEqual(report['sharedInputsTrainValidation'], 6)
        self.assertFalse(report['inputIsolationPassed'])
        with redirect_stdout(io.StringIO()):
            code = dataset_main(['--experiment', str(self.output), '--manifest', str(self.manifest),
                                 '--profile', 'competency-review-v1'])
        self.assertEqual(code, 2)

    def test_training_inspection_rejects_contamination_before_gpu_or_lock(self):
        cache = self.configure()
        self.collide_validation_inputs()
        with self.assertRaisesRegex(AuditError, 'Duplicate model inputs'):
            inspect_experiment(self.output, cache, self.manifest)
        self.assertFalse((self.output / '.coach-ml.lock').exists())

    def test_missing_lazy_training_dependency_returns_controlled_error_and_releases_lock(self):
        cache = self.configure()
        error = io.StringIO()
        with patch('coach_ml.lora.run', side_effect=ImportError('missing torch')), redirect_stderr(error):
            code = training_main(['--experiment', str(self.output), '--model-cache', str(cache),
                                  '--manifest', str(self.manifest), '--stage', 'prepare'])
        self.assertEqual(code, 2)
        self.assertIn('Training dependencies unavailable', error.getvalue())
        self.assertFalse((self.output / '.coach-ml.lock').exists())
        self.assertFalse((self.output / 'freeze.json').exists())

    def test_optimized_python_is_rejected_before_reading_training_files(self):
        import subprocess
        import sys
        result = subprocess.run([sys.executable, '-B', '-O', '-m', 'coach_ml', 'train', '--stage', 'prepare',
            '--experiment', str(self.root / 'absent'), '--model-cache', str(self.root),
            '--manifest', str(self.manifest)], capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 2)
        self.assertIn('Optimized Python', result.stderr)
        self.assertFalse((self.root / 'absent').exists())

    def test_runtime_invariant_failure_is_controlled_and_releases_lock(self):
        cache = self.configure()
        error = io.StringIO()
        with patch('coach_ml.lora.run', side_effect=AssertionError('Frozen environment changed')), redirect_stderr(error):
            code = training_main(['--experiment', str(self.output), '--model-cache', str(cache),
                                  '--manifest', str(self.manifest), '--stage', 'prepare'])
        self.assertEqual(code, 2)
        self.assertIn('Training invariant failed', error.getvalue())
        self.assertFalse((self.output / '.coach-ml.lock').exists())

    def test_sample_count_and_invalid_numeric_config_are_rejected(self):
        cache = self.configure()
        config_file = self.output / 'config.json'
        config = json.loads(config_file.read_text())
        config['samples'] = 7
        config_file.write_text(json.dumps(config))
        with self.assertRaisesRegex(AuditError, 'sample count'):
            inspect_experiment(self.output, cache, self.manifest)
        config['samples'] = True
        config_file.write_text(json.dumps(config))
        with self.assertRaisesRegex(AuditError, 'samples'):
            load_config(self.output)

    def test_legacy_runtime_is_rejected_before_any_ml_import_or_write(self):
        self.output.mkdir()
        (self.output / 'freeze.json').write_text(json.dumps({'fingerprint': {}}))
        with redirect_stderr(io.StringIO()):
            result = training_main(['--experiment', str(self.output), '--model-cache', str(self.root),
                                    '--manifest', str(self.manifest), '--stage', 'run'])
        self.assertEqual(result, 2)
        self.assertFalse((self.output / '.coach-ml.lock').exists())

    def test_cli_help_and_runtime_import_do_not_initialize_torch(self):
        import sys
        import coach_ml.lora
        with redirect_stdout(io.StringIO()), self.assertRaises(SystemExit) as stop:
            cli.main(['train', '--help'])
        self.assertEqual(stop.exception.code, 0)
        self.assertNotIn('torch', sys.modules)

    def test_evaluation_distinguishes_boolean_from_number_and_rejects_duplicate_keys(self):
        expected = expected_response(project_request(scene(), 'economy-snapshot'))
        self.assertTrue(score_response(json.dumps(expected), expected)['observationCorrect'])
        wrong = copy.deepcopy(expected)
        wrong['observation']['lastHits'] = True
        self.assertFalse(score_response(json.dumps(wrong), expected)['observationCorrect'])
        self.assertFalse(score_response('{"x":1,"x":2}', expected)['valid'])


if __name__ == '__main__':
    unittest.main()
