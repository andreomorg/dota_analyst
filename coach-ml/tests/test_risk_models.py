"""Death-risk training and evaluation on a synthetic export with a known signal (CPU, needs the risk extra)."""
import importlib.util
import json
import random
import tempfile
import unittest
from pathlib import Path

from coach_ml.core.validation import AuditError
from coach_ml.risk.export import FEATURE_VERSION, SITUATION_BY_VERSION, export

HAS_SKLEARN = importlib.util.find_spec('sklearn') is not None


def synthetic_match(match_id, rng, rows=300):
    """Heroes far from every ally on the enemy side die far more often: the signal the models must find."""
    samples = []
    for index in range(rows):
        far = rng.random() < 0.3
        enemy_side = rng.random() < 0.4
        # A clear signal: with 35% against 3% the best possible ranking only reached PR-AUC ≈ 0.24 and ROC ≈ 0.76, below
        # the thresholds asserted below (the test could never pass).
        dies = rng.random() < (0.6 if far and enemy_side else 0.01)
        situation = {column: 0 for column in SITUATION_BY_VERSION[FEATURE_VERSION]}
        situation.update({'heroId': 1 + index % 10, 'team': 'radiant' if index % 10 < 5 else 'dire',
                          'seconds': (index // 10) * 10, 'role': 1 + index % 5, 'roleInferred': False,
                          'region': 'enemyJungle' if enemy_side else 'ownJungle', 'onEnemySide': enemy_side,
                          'nearestAllyDistance': rng.randint(4000, 9000) if far else rng.randint(100, 1500),
                          'secondsSinceLastDeath': None,
                          'hiddenNearestEnemyDistance': rng.randint(300, 1200) if dies else 5000})
        samples.append({'situation': situation, 'diesSoon': dies,
                        'diedAtSeconds': (index // 10) * 10 + 5 if dies else None})
    return {'featureVersion': FEATURE_VERSION, 'matchId': match_id, 'sampleEverySeconds': 10, 'horizonSeconds': 15,
            'samples': samples}


def write_export(folder, matches=40, validation=10):
    rng = random.Random(7)
    manifest = [{'matchId': str(i), 'split': 'train'} for i in range(1, matches + 1)]
    manifest += [{'matchId': str(i), 'split': 'validation-new'} for i in range(matches + 1, matches + validation + 1)]
    manifest += [{'matchId': str(matches + validation + 1), 'split': 'test-new'}]
    answers = {match['matchId']: (200, synthetic_match(match['matchId'], rng), None) for match in manifest}
    export(manifest, lambda match_id: answers[match_id], folder, sleep=lambda _s: None, log=lambda _m: None)


@unittest.skipUnless(HAS_SKLEARN, 'needs the risk extra (scikit-learn)')
class RiskModelTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from coach_ml.risk.train import train
        cls.folder = tempfile.TemporaryDirectory()
        root = Path(cls.folder.name)
        cls.export, cls.models = root / 'export', root / 'models'
        write_export(cls.export)
        cls.training = train(cls.export, cls.models, log=lambda _m: None, iterations=[50, 100])

    @classmethod
    def tearDownClass(cls):
        cls.folder.cleanup()

    def test_split_never_puts_a_match_on_two_sides_and_covers_every_row(self):
        import numpy as np

        from coach_ml.risk.train import split_matches
        groups = np.repeat(np.arange(30), 7)
        masks = split_matches(groups, np)
        self.assertTrue((sum(mask.astype(int) for mask in masks) == 1).all())
        for first in range(3):
            for second in range(first + 1, 3):
                self.assertFalse(set(groups[masks[first]]) & set(groups[masks[second]]))

    def test_training_records_its_inputs_and_never_the_validation(self):
        self.assertEqual(set(self.training['export']), {'train', 'validation-new', 'test-new'})
        # Only the development partitions the export has (this one has no train-known).
        self.assertEqual(self.training['trainedOn'], ['train'])
        self.assertFalse(self.training['humanReviewed'])
        self.assertTrue((self.models / 'training.json').exists())

    def test_hero_variant_keeps_the_original_models_columns(self):
        from coach_ml.risk.data import HIDDEN_BY_VERSION, SIMPLE, VISIBLE_BY_VERSION
        from coach_ml.risk.train import ITERATIONS, ml

        visible = VISIBLE_BY_VERSION[FEATURE_VERSION]
        training = json.loads((self.models / 'training.json').read_text(encoding='utf-8'))
        self.assertEqual(training['columns']['boostedVisibleHero'], visible + ['heroId'])
        self.assertEqual(training['columns']['boostedVisible'], visible)
        self.assertEqual(training['columns']['boostedOmniscient'], visible + HIDDEN_BY_VERSION[FEATURE_VERSION])
        self.assertEqual(training['columns']['logistic'], SIMPLE)
        self.assertEqual(ITERATIONS, [100, 200, 400, 800, 1600, 3200])
        models = ml()['joblib'].load(self.models / 'models.joblib')
        self.assertTrue(models['boostedVisibleHero'].model.categorical_features[-1])

    def test_hero_ids_outside_the_category_range_are_refused_before_training(self):
        from unittest.mock import patch

        from coach_ml.risk.data import load
        from coach_ml.risk.train import ml, train

        lib = ml()
        for hero_id in (-1, 255, 300):
            def invalid_load(*args, hero_id=hero_id, **kwargs):
                features, labels, groups, extra = load(*args, **kwargs)
                extra['heroId'][0] = hero_id
                return features, labels, groups, extra

            with self.subTest(hero_id=hero_id), tempfile.TemporaryDirectory() as folder:
                output = Path(folder) / 'models'
                with patch('coach_ml.risk.train.load', side_effect=invalid_load):
                    with self.assertRaisesRegex(AuditError, 'heroId >= 0 and < 255'):
                        train(self.export, output, lib=lib, iterations=[1], log=lambda _m: None)
                self.assertFalse(output.exists())

    def test_the_boosted_model_finds_the_signal_and_beats_the_constant(self):
        from coach_ml.risk.evaluate import evaluate
        with tempfile.TemporaryDirectory() as folder:
            report = evaluate(self.models, self.export, Path(folder) / 'report')
        models = report['models']
        self.assertIn('boostedVisibleHero', models)
        self.assertGreater(models['boostedVisible']['overall']['prAuc'], models['constant']['overall']['prAuc'] + 0.2)
        self.assertGreater(models['boostedVisible']['overall']['rocAuc'], 0.8)
        self.assertEqual(report['partitions'], ['validation-new'])
        bins = models['boostedVisible']['calibration']
        self.assertEqual(sum(part['rows'] for part in bins), models['boostedVisible']['overall']['rows'])

    def test_the_final_test_needs_the_flag_and_the_flag_only_opens_it(self):
        from coach_ml.risk.evaluate import evaluate
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(AuditError):
                evaluate(self.models, self.export, Path(folder) / 'a', partitions=['test-new'])
            with self.assertRaises(AuditError):
                evaluate(self.models, self.export, Path(folder) / 'b', partitions=['validation-new'], final=True)

    def test_a_changed_model_file_is_refused(self):
        import shutil

        from coach_ml.risk.evaluate import evaluate
        with tempfile.TemporaryDirectory() as folder:
            copy = Path(folder) / 'models'
            shutil.copytree(self.models, copy)
            with (copy / 'models.joblib').open('ab') as stream:
                stream.write(b'x')
            with self.assertRaises(AuditError):
                evaluate(copy, self.export, Path(folder) / 'report')

    def test_explanations_rank_the_signal_and_explain_each_focus_death(self):
        from coach_ml.risk.data import VISIBLE_BY_VERSION
        from coach_ml.risk.explain import NOT_ACTIONABLE, explain
        VISIBLE = VISIBLE_BY_VERSION[FEATURE_VERSION]
        with tempfile.TemporaryDirectory() as folder:
            report = explain(self.models, self.export, Path(folder) / 'explain', ['validation-new'], focus=[(41, 1)])
        ranked = [item['column'] for item in report['importance']]
        self.assertEqual(sorted(ranked), sorted(VISIBLE))
        drops = [item['prAucDrop'] for item in report['importance']]
        self.assertEqual(drops, sorted(drops, reverse=True))
        # The synthetic deaths depend on the nearest ally and the enemy side: one of them leads.
        self.assertIn(ranked[0], {'nearestAllyDistance', 'onEnemySide', 'region'})
        self.assertFalse(set(report['dependence']) & (NOT_ACTIONABLE | {'region'}))
        self.assertTrue(report['deaths'])
        for death in report['deaths']:
            self.assertEqual((death['matchId'], death['heroId']), ('41', 1))
            self.assertTrue(death['riskBefore'])
            self.assertFalse({standout['column'] for standout in death['standouts']} & NOT_ACTIONABLE)
            # Each reason lowers the risk when set to its safe value, the strongest first.
            risk = death['riskBefore'][-1]['risk']
            changes = [standout['riskIfSafe'] for standout in death['standouts']]
            self.assertTrue(all(change < risk + 1e-3 for change in changes))
            self.assertEqual(changes, sorted(changes))

    def test_only_sensible_changes_become_advice(self):
        from coach_ml.risk.data import REGIONS
        from coach_ml.risk.explain import kind_of
        self.assertEqual(kind_of('nearestAllyDistance', 8000, 900), 'advice')   # get closer to an ally
        self.assertEqual(kind_of('nearestAllyDistance', 400, 5000), 'context')  # "stay away" is not advice
        self.assertEqual(kind_of('onEnemySide', 1, 0), 'advice')
        self.assertEqual(kind_of('level', 3, 5), 'context')
        self.assertEqual(kind_of('region', REGIONS.index('enemyJungle'), REGIONS.index('ownJungle')), 'advice')
        self.assertEqual(kind_of('region', REGIONS.index('topLane'), REGIONS.index('ownJungle')), 'context')

    def test_explanations_never_open_the_final_test(self):
        from coach_ml.risk.explain import explain
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(AuditError):
                explain(self.models, self.export, Path(folder) / 'explain', ['test-new'])

    def test_metrics_match_hand_computed_values(self):
        import numpy as np

        from coach_ml.risk.evaluate import calibration, metrics
        from coach_ml.risk.train import ml
        lib = ml()
        labels = np.array([0, 0, 1, 1])
        perfect = metrics(labels, np.array([0.1, 0.2, 0.8, 0.9]), lib)
        self.assertEqual((perfect['prAuc'], perfect['rocAuc']), (1.0, 1.0))
        # Brier = mean squared error: (0.01 + 0.04 + 0.04 + 0.01) / 4.
        self.assertEqual(perfect['brier'], 0.025)
        constant = metrics(labels, np.full(4, 0.5), lib)
        self.assertEqual((constant['prAuc'], constant['rocAuc'], constant['brier']), (0.5, 0.5, 0.25))
        self.assertIsNone(metrics(np.array([0, 0]), np.array([0.1, 0.2]), lib)['rocAuc'])
        bins = calibration(labels, np.array([0.1, 0.2, 0.8, 0.9]), np, bins=2)
        self.assertEqual(bins, [{'predicted': 0.15, 'observed': 0.0, 'rows': 2}, {'predicted': 0.85, 'observed': 1.0, 'rows': 2}])


class RiskDataTests(unittest.TestCase):
    def test_a_changed_export_file_is_refused(self):
        from coach_ml.risk.data import read_export
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder) / 'export'
            write_export(root, matches=2, validation=1)
            with (root / 'train.csv.gz').open('ab') as stream:
                stream.write(b'x')
            with self.assertRaises(AuditError):
                read_export(root)

    def test_unknown_activity_is_missing_not_zero(self):
        from coach_ml.risk.data import encode
        row = {'activityKnown': '0', 'itemUsesLast30Seconds': '0', 'hiddenEnemySmokeSecondsAgo': ''}
        self.assertNotEqual(encode(row, 'itemUsesLast30Seconds'), encode(row, 'itemUsesLast30Seconds'))  # NaN
        self.assertEqual(encode(row | {'activityKnown': '1'}, 'itemUsesLast30Seconds'), 0.0)

    def test_the_model_columns_come_from_its_training_not_the_current_code(self):
        from coach_ml.risk.data import VISIBLE_BY_VERSION
        # Version 1 models keep their 22 visible columns; version 2 adds the recent activity.
        self.assertEqual(len(VISIBLE_BY_VERSION[1]), 22)
        self.assertEqual(VISIBLE_BY_VERSION[2][:22], VISIBLE_BY_VERSION[1])

    def test_inferred_roles_are_hidden_before_ten_minutes(self):
        from coach_ml.risk.data import encode
        row = {'role': '3', 'roleInferred': '1', 'seconds': '590'}
        self.assertNotEqual(encode(row, 'role'), encode(row, 'role'))  # NaN
        self.assertEqual(encode(row | {'seconds': '600'}, 'role'), 3.0)
        self.assertEqual(encode(row | {'roleInferred': '0'}, 'role'), 3.0)
        # Version 2 (with roleConfidence): estimated at that second, so kept before 10:00 too.
        self.assertEqual(encode(row | {'roleConfidence': '0.4'}, 'role'), 3.0)

    def test_manifest_records_every_partition(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder) / 'export'
            write_export(root, matches=2, validation=1)
            manifest = json.loads((root / 'export-manifest.json').read_text(encoding='utf-8'))
            self.assertEqual(set(manifest['partitions']), {'train', 'validation-new', 'test-new'})


if __name__ == '__main__':
    unittest.main()
