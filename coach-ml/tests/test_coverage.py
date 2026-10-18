import json
import io
from contextlib import redirect_stdout, redirect_stderr
import unittest
from unittest.mock import patch

from coach_ml.audit import AuditError
from coach_ml.coverage import audit_coverage, phase_for, main
from coach_ml.hashing import file_hash
import test_project


class CoverageTests(unittest.TestCase):
    def setUp(self):
        self.fixture = test_project.ProjectIntegrationTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.temp.cleanup)

    def report(self):
        self.fixture.build()
        return audit_coverage(self.fixture.output, self.fixture.manifest, 'competency-review-v1')

    def test_six_tasks_on_one_player_scene_do_not_become_six_matches(self):
        result = self.report()
        train = result['partitions']['train']
        self.assertEqual((train['examples'], train['playerScenes'], train['matches']), (6, 1, 1))
        self.assertEqual(train['dimensions']['hero']['41']['playerScenes'], 1)
        self.assertFalse(result['tacticalQualityApproved'])

    def test_unseen_combinations_are_separate_from_unseen_heroes(self):
        result = self.report()
        self.assertEqual(result['validationOnlyHeroes'], [])
        self.assertEqual(len(result['validationCellsAbsentFromTrain']), 6)
        self.assertEqual(result['unseenValidationExamples'], 6)
        self.assertEqual(result['unseenValidationPlayerScenes'], 1)
        self.assertEqual(result['unseenValidationMatches'], 1)

    def test_unseen_hero_is_reported_without_using_external_hero_names(self):
        scene = test_project.scene('200', 'validation-new')
        scene['heroId'] = 42
        scene['retrospectiveOutcome']['id'] = 'death-42-110'
        scene['id'] = '200/42/death-42-110'
        facts = scene['input']['atOrBeforeAnchor']
        facts['positions'][0]['heroId'] = 42
        facts['recentPurchases'][0]['heroId'] = 42
        facts['recentAbilityUpgrades'][0]['heroId'] = 42
        file = self.fixture.source / 'model-corpus/validation-new.scenes.jsonl'
        file.write_text(json.dumps(scene) + '\n')
        audit_file = self.fixture.source / 'audit.json'
        audit = json.loads(audit_file.read_text())
        audit['corpusFiles']['model-corpus/validation-new.scenes.jsonl']['sha256'] = file_hash(file)
        audit_file.write_text(json.dumps(audit))
        result = self.report()
        self.assertEqual(result['validationOnlyHeroes'], [42])
        self.assertEqual(result['trainOnlyHeroes'], [41])

    def test_phase_boundaries_and_boolean_clock(self):
        self.assertEqual([phase_for(s) for s in [0, 599, 600, 1499, 1500]],
            ['before-10m', 'before-10m', '10m-to-25m', '10m-to-25m', 'after-25m'])
        for value in [-1, True, None]:
            with self.subTest(value=value), self.assertRaises(AuditError):
                phase_for(value)

    def test_inferred_roles_and_missing_patch_are_not_presented_as_verified_knowledge(self):
        result = self.report()
        train = result['partitions']['train']
        self.assertEqual(train['dimensions']['roleOrigin']['declared-not-inferred']['playerScenes'], 1)
        self.assertEqual(train['examplesWithoutProjectedPatch'], 6)
        self.assertFalse(result['patchCoverageAnalyzed'])

    def test_contaminated_inputs_are_rejected_before_coverage_is_reported(self):
        self.fixture.build()
        self.fixture.collide_validation_inputs()
        with self.assertRaisesRegex(AuditError, 'isolated inputs'):
            audit_coverage(self.fixture.output, self.fixture.manifest, 'competency-review-v1')

    def test_inputs_changed_after_validation_are_detected(self):
        import coach_ml.coverage as module
        self.fixture.build()
        original = module.audit_experiment

        def changing_audit(*args):
            result = original(*args)
            with (self.fixture.output / 'validation-metadata.json').open('a') as file:
                file.write(' ')
            return result

        with patch.object(module, 'audit_experiment', side_effect=changing_audit):
            with self.assertRaisesRegex(AuditError, 'Inputs changed'):
                audit_coverage(self.fixture.output, self.fixture.manifest, 'competency-review-v1')

    def test_cli_saves_a_new_report_and_prints_a_small_summary(self):
        self.fixture.build()
        destination = self.fixture.root / 'coverage.json'
        stdout = io.StringIO()
        with redirect_stdout(stdout):
            code = main(['--experiment', str(self.fixture.output), '--manifest', str(self.fixture.manifest),
                         '--profile', 'competency-review-v1', '--output', str(destination)])
        self.assertEqual(code, 0)
        self.assertLess(len(stdout.getvalue()), 1000)
        self.assertEqual(json.loads(destination.read_text())['partitions']['train']['playerScenes'], 1)

    def test_cli_does_not_overwrite_an_existing_report(self):
        destination = self.fixture.root / 'coverage.json'
        destination.write_text('keep')
        with redirect_stderr(io.StringIO()):
            code = main(['--experiment', str(self.fixture.root / 'missing'), '--manifest', str(self.fixture.manifest),
                         '--output', str(destination)])
        self.assertEqual(code, 2)
        self.assertEqual(destination.read_text(), 'keep')
