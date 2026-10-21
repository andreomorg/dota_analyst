"""Next-region model on a synthetic export with a known signal (CPU, needs the risk extra)."""
import importlib.util
import random
import tempfile
import unittest
from pathlib import Path

from coach_ml.core.validation import AuditError
from coach_ml.risk.export import FEATURE_VERSION, SITUATION_BY_VERSION, export

HAS_SKLEARN = importlib.util.find_spec('sklearn') is not None


def synthetic_match(match_id, rng, rows=300):
    """Supports (roles 4-5) go to the river after 10:00; everyone else stays in its region. Some die."""
    samples = []
    for index in range(rows):
        role, second = 1 + index % 5, (index // 10) * 40  # 0:00 to 19:20
        region = rng.choice(['topLane', 'bottomLane', 'ownJungle'])
        later = 'river' if role >= 4 and second >= 600 else region
        situation = {column: 0 for column in SITUATION_BY_VERSION[FEATURE_VERSION]}
        situation.update({'heroId': 1 + index % 10, 'team': 'radiant', 'seconds': second, 'role': role,
                          'roleInferred': False, 'region': region, 'onEnemySide': False, 'nearestAllyDistance': None,
                          'secondsSinceLastDeath': None, 'hiddenNearestEnemyDistance': None})
        samples.append({'situation': situation, 'regionLater': None if rng.random() < 0.05 else later})
    return {'featureVersion': FEATURE_VERSION, 'matchId': match_id, 'sampleEverySeconds': 10, 'horizonSeconds': 30,
            'samples': samples}


def write_export(folder):
    rng = random.Random(3)
    manifest = [{'matchId': str(i), 'split': 'train'} for i in range(1, 31)]
    manifest += [{'matchId': str(i), 'split': 'validation-new'} for i in range(31, 39)]
    answers = {match['matchId']: (200, synthetic_match(match['matchId'], rng), None) for match in manifest}
    export(manifest, lambda match_id: answers[match_id], folder, sleep=lambda _s: None, log=lambda _m: None,
           kind='next-region')


@unittest.skipUnless(HAS_SKLEARN, 'needs the risk extra (scikit-learn)')
class NextRegionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from coach_ml.risk.next_region import train
        cls.folder = tempfile.TemporaryDirectory()
        root = Path(cls.folder.name)
        cls.export, cls.models = root / 'export', root / 'models'
        write_export(cls.export)
        cls.training = train(cls.export, cls.models, log=lambda _m: None, iterations=[30, 60])

    @classmethod
    def tearDownClass(cls):
        cls.folder.cleanup()

    def test_the_boosted_model_beats_both_baselines(self):
        from coach_ml.risk.next_region import evaluate
        with tempfile.TemporaryDirectory() as folder:
            report = evaluate(self.models, self.export, Path(folder) / 'report')
        models = report['models']
        # Role x minute alone cannot know the current region; the region alone cannot know the role and the clock.
        self.assertGreater(models['boosted']['overall']['top1'], models['transition']['overall']['top1'])
        self.assertGreater(models['boosted']['overall']['top1'], models['roleMinute']['overall']['top1'])
        self.assertLess(models['boosted']['overall']['logLoss'], models['roleMinute']['overall']['logLoss'])
        self.assertEqual(len(models['boosted']['overall']['calibration']), 9)

    def test_a_death_risk_export_is_not_a_next_region_one(self):
        import numpy as np

        from coach_ml.risk.data import load
        with self.assertRaises(AuditError):
            load(self.export, ['train'], ['seconds'], np, kind='death-risk')

    def test_scores_match_hand_computed_values(self):
        import numpy as np

        from coach_ml.risk.next_region import scores
        from coach_ml.risk.train import ml
        probabilities = np.zeros((2, 9))
        probabilities[0, [0, 1, 2]] = [0.5, 0.3, 0.2]   # true class 1: second -> top-3 only
        probabilities[1, [3, 4]] = [0.9, 0.1]           # true class 3: first
        result = scores(np.array([1, 3]), probabilities, ml())
        self.assertEqual((result['top1'], result['top3']), (0.5, 1.0))

    def test_hints_need_all_three_conditions(self):
        import numpy as np

        from coach_ml.risk.data import DESTINATIONS
        from coach_ml.risk.hints import candidates
        river, top, dead = DESTINATIONS.index('river'), DESTINATIONS.index('topLane'), DESTINATIONS.index('dead')
        # One hero, samples at 0, 60 (the next minute of 0) and 120; the model says "river" at 80%.
        probabilities = np.zeros((4, 9))
        probabilities[:, river], probabilities[:, top] = 0.8, 0.05
        probabilities[:, dead] = 0.15
        labels = np.array([top, top, dead, river])
        groups = np.array([1, 1, 1, 1])
        extra = {'heroId': np.array([1, 1, 1, 1]), 'seconds': np.array([0, 60, 120, 180]), 'role': ['1', '1', '1', '1']}
        net_worth = np.array([1000, 1100, 2000, 2600])  # gains: 100, 900, 600 -> usual (median) 600
        found = candidates(probabilities, labels, groups, extra, net_worth, [(1, 1)], np)
        # 0: went top (5%), river 80%, gained 100 < 300 -> candidate. 60: went top but gained 900 -> no.
        # 120: died (15%: not unlikely enough) -> no. 180: went where the model said -> no.
        self.assertEqual([(hint['seconds'], hint['consequence']) for hint in found], [(0, 'lowNetWorthGain')])
        self.assertEqual((found[0]['usualDestination'], found[0]['usualNetWorthGain']), ('river', 600))

    def test_the_final_test_needs_the_flag(self):
        from coach_ml.risk.next_region import evaluate
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(AuditError):
            evaluate(self.models, self.export, Path(folder) / 'report', partitions=['test-new'])


if __name__ == '__main__':
    unittest.main()
