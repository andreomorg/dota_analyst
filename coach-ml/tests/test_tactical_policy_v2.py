import copy
import json
import unittest

from coach_ml.core.validation import AuditError
from coach_ml.tactical.audit import validate_request
from coach_ml.tactical.contracts import validate_candidate
from coach_ml.tactical.policy_v2 import target_for
from coach_ml.tactical.projection import project_scene
from coach_ml.tactical.situated import project_situated_scene


def context(values=None):
    return {'schemaVersion': 5, 'heroes': [],
            'decision': {'schemaVersion': 1, 'heroId': 1,
                         'role': {'position': 1, 'source': 'replay', 'isInferred': False},
                         'patch': {'matchPatch': None, 'source': 'unknown',
                                   'trainingPatch': None, 'mechanicsVerified': False},
                         'scenes': [{'atOrBeforeAnchor': {
                             'anchorSeconds': 100, 'positions': [], 'missingHeroPositions': 10,
                             'economy': None, 'recentPurchases': [],
                             'recentAbilityUpgrades': [], 'recentObjectives': None}}]},
            'situations': [{'anchorSeconds': 100, 'situation': values or {}}]}


CASES = [
    ({'visibleEnemiesWithinFightRange': 4, 'alliesWithinFightRange': 2}, ['2', '4']),
    ({'healthShare': .23, 'visibleEnemiesWithinFightRange': 1,
      'nearestVisibleEnemyDistance': 875}, ['23%', '875']),
    ({'escapeItemReady': True, 'damageFromHeroesLast6Seconds': 219}, ['219']),
    ({'onEnemySide': True, 'nearestAllyDistance': 1700, 'missingEnemies': 3,
      'missingEnemyLastSeenSecondsAgo': 14}, ['1700', '3', '14']),
    ({'aheadOfOwnWave': 320, 'inOwnObserverVision': False, 'missingEnemies': 2}, ['2']),
    ({'ownBuildingsUnderAttack': 2, 'teleportReady': True}, ['2']),
    ({'ultimateReady': True, 'visibleEnemiesWithinFightRange': 2}, ['2']),
    ({'roshanAlive': True, 'teamNetWorthLead': 3456, 'hasAegis': False}, ['3456']),
    # v5.1: orders, escape routes, pings.
    ({'intentDestinationOnEnemySide': True, 'intentDestinationAdvance': 1800, 'missingEnemies': 3,
      'towerSeenShare': .25}, ['1800', '3', '25%']),
    ({'healthShare': .45, 'towerPathLength': 2400, 'towerNearestSeenEnemy': 700, 'allyPathLength': 900},
     ['45%', '2400', '700', '900']),
    ({'allyPingsNearLast10': 3, 'nearestAllyDistance': 2100}, ['3', '2100']),
]


class PolicyTests(unittest.TestCase):
    def test_rules_numbers_contract_and_determinism(self):
        for index, (values, numbers) in enumerate(CASES, 1):
            with self.subTest(rule=index):
                request = project_situated_scene(context(values), 0)
                before = copy.deepcopy(request)
                rule, target = target_for(request)
                self.assertEqual(rule, f'R{index}')
                self.assertEqual(target_for(request), (rule, target))
                self.assertEqual(request, before)
                self.assertEqual(validate_candidate(request, target), target)
                self.assertEqual(target['disposition'], 'conditional-review')
                self.assertEqual(target['preference'], 'undetermined')
                text = json.dumps(target, ensure_ascii=False)
                for number in numbers:
                    self.assertIn(number, text)
                for forbidden in ('hidden', 'morreu', 'venceu', 'luta vencida'):
                    self.assertNotIn(forbidden, text.lower())
                for alternative in target['alternatives']:
                    self.assertTrue(set(alternative['unknowns']) <= set(request['unknowns']))
                    self.assertIn('situation', alternative['evidenceIds'])
                    self.assertTrue(all(c.startswith('Verificar se ') for c in alternative['conditions']))

    def test_null_required_fields_do_not_trigger(self):
        required = [list(CASES[0][0]), ['healthShare', 'visibleEnemiesWithinFightRange'],
                    list(CASES[2][0]), ['onEnemySide', 'nearestAllyDistance', 'missingEnemies'],
                    list(CASES[4][0]), list(CASES[5][0]), list(CASES[6][0]), list(CASES[7][0]),
                    ['intentDestinationOnEnemySide', 'missingEnemies', 'towerSeenShare'],
                    ['healthShare', 'towerNearestSeenEnemy'], list(CASES[10][0])]
        for index, fields in enumerate(required):
            for field in fields:
                values = {**CASES[index][0], field: None}
                with self.subTest(rule=index + 1, field=field):
                    self.assertEqual(target_for(project_situated_scene(context(values), 0))[0], 'R0')

    def test_priority_and_alternate_escape(self):
        values = {**CASES[0][0], 'ultimateReady': True}
        self.assertEqual(target_for(project_situated_scene(context(values), 0))[0], 'R1')
        values = {'myEscapeReady': True, 'damageFromHeroesLast6Seconds': 17}
        self.assertEqual(target_for(project_situated_scene(context(values), 0))[0], 'R3')

    def test_r0(self):
        request = project_situated_scene(context(), 0)
        rule, target = target_for(request)
        self.assertEqual(rule, 'R0')
        self.assertEqual(target['disposition'], 'insufficient')
        self.assertEqual(target['alternatives'], [])
        validate_candidate(request, target)

    def test_thresholds_and_missing_optional_details(self):
        for values in (
                {'visibleEnemiesWithinFightRange': 3, 'alliesWithinFightRange': 2},
                {'healthShare': .31, 'visibleEnemiesWithinFightRange': 1},
                {'escapeItemReady': True, 'damageFromHeroesLast6Seconds': 0},
                {'onEnemySide': True, 'nearestAllyDistance': 1500, 'missingEnemies': 3},
                {'aheadOfOwnWave': -1, 'inOwnObserverVision': False, 'missingEnemies': 2},
                {'roshanAlive': True, 'teamNetWorthLead': 2999, 'hasAegis': False}):
            self.assertEqual(target_for(project_situated_scene(context(values), 0))[0], 'R0')
        for index, field in ((1, 'nearestVisibleEnemyDistance'), (3, 'missingEnemyLastSeenSecondsAgo')):
            values = {**CASES[index][0], field: None}
            rule, target = target_for(project_situated_scene(context(values), 0))
            self.assertEqual(rule, f'R{index + 1}')
            self.assertIn('desconhecido', target['opportunity'])

    def test_four_deterministic_wordings(self):
        for values, _ in CASES:
            openings = set()
            for anchor in range(100, 180):
                source = context(values)
                source['situations'][0]['anchorSeconds'] = anchor
                source['decision']['scenes'][0]['atOrBeforeAnchor']['anchorSeconds'] = anchor
                _, target = target_for(project_situated_scene(source, 0))
                openings.add(target['opportunity'].split()[0])
            self.assertEqual(len(openings), 4)

    def test_profile_isolation_and_hidden(self):
        source = context(CASES[0][0])
        v5 = project_situated_scene(source, 0)
        v1 = project_scene(source['decision'], source['decision']['scenes'][0])
        for request, profile, wrong in ((v1, 'tactical-review-v1', 'tactical-review-v5'),
                                        (v5, 'tactical-review-v5', 'tactical-review-v1')):
            self.assertEqual(validate_request(request, profile), request)
            with self.assertRaises(AuditError):
                validate_request(request, wrong)
        v5['facts'][-1]['value'] = [{'hiddenValue': None}]
        with self.assertRaises(AuditError):
            validate_request(v5)


if __name__ == '__main__':
    unittest.main()
