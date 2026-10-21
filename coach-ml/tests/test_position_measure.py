"""The position estimator report from fake API answers (standard library only)."""
import unittest

from coach_ml.risk.positions import measure


def hero(hero_id, truth, old, *estimates):
    return {'heroId': hero_id, 'team': 'radiant', 'roleQueue': truth, 'oldRule': old,
            'estimates': [{'second': second, 'position': position, 'confidence': 0.5} for second, position in estimates]}


class PositionMeasureTests(unittest.TestCase):
    def test_accuracy_per_checkpoint_position_and_the_old_rule(self):
        answers = {
            '1': (200, {'matchId': '1', 'heroes': [hero(1, 1, 1, (60, 5), (600, 1)), hero(2, 5, 1, (60, 5), (600, 5))]}, None),
            '2': (200, {'matchId': '2', 'heroes': [hero(3, None, 2, (60, 2))]}, None),  # no role queue: not measured
            '3': (404, None, None),
        }
        report = measure([{'matchId': key, 'split': 'train'} for key in answers], lambda match_id: answers[match_id],
                         sleep=lambda _s: None, log=lambda _m: None)

        self.assertEqual((report['matches'], report['withRoleQueue'], report['skipped']), (2, 1, {'noDocument': 1}))
        self.assertEqual(report['oldRuleAccuracy'], 0.5)
        self.assertEqual(report['checkpoints']['60']['accuracy'], 0.5)
        self.assertEqual(report['checkpoints']['600']['accuracy'], 1.0)
        self.assertEqual(report['checkpoints']['60']['byPosition']['1'], 0.0)
        self.assertEqual(report['checkpoints']['60']['confusion']['1']['5'], 1)
        self.assertIsNone(report['checkpoints']['60']['byPosition']['3'])


if __name__ == '__main__':
    unittest.main()
