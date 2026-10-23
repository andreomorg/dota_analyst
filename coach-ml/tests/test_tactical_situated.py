import copy
import unittest

from coach_ml.core.validation import AuditError
from coach_ml.tactical.pipeline import SYSTEM
from coach_ml.tactical.projection import project_scene
from coach_ml.tactical.prompt_v5 import SYSTEM_V5
from coach_ml.tactical.situated import FIELDS, project_situated_scene


def fixture():
    return {
        'schemaVersion': 5,
        'decision': {
            'schemaVersion': 1, 'heroId': 1,
            'role': {'position': 1, 'source': 'replay', 'isInferred': False},
            'patch': {'matchPatch': None, 'source': 'unknown',
                      'trainingPatch': None, 'mechanicsVerified': False},
            'scenes': [{'retrospectiveEpisode': {'id': 'never-export', 'eventSeconds': 110},
                        'atOrBeforeAnchor': {
                            'anchorSeconds': 100, 'positions': [], 'missingHeroPositions': 10,
                            'economy': {'sampleSeconds': 60, 'netWorth': 900, 'lastHits': 10},
                            'recentPurchases': [], 'recentAbilityUpgrades': [],
                            'recentObjectives': None}}]},
        'heroes': [{'heroId': i, 'name': 'Catálogo ' + str(i),
                    'team': 'dire' if i <= 5 else 'radiant', 'roles': ['support']}
                   for i in range(1, 11)],
        'situations': [{'anchorSeconds': 100, 'situation': dict.fromkeys(FIELDS)}],
    }


class SituatedTests(unittest.TestCase):
    def test_contract_field_order(self):
        expected = '''level netWorth gold healthShare manaShare stunned silenced activeDisables activeSlows
teleportReady hasEscapeItem escapeItemReady ultimateReady myDisableReady myEscapeReady buybackReady
enemiesAlive visibleEnemies visibleEnemiesWithinFightRange nearestVisibleEnemyDistance missingEnemies
missingEnemyLastSeenSecondsAgo nearestVisibleEnemyKillsOnMe alliesAlive nearestAllyDistance alliesWithinFightRange
lowestAllyHealthShare alliesWithTeleportReady region onEnemySide onEnemyHighGround terrainHeightAdvantage
terrainOpenness inOwnObserverVision teamLocalVisionCoverage nearestOwnTowerDistance nearestEnemyTowerDistance
aheadOfOwnWave waveClashPoint waveClashPastOwnTower ownBuildingsUnderAttack teamNetWorthLead teamExperienceLead
isNight secondsSinceRoshanKill roshanAlive hasAegis enemyHasAegis recentDeaths enemyKillsLastMinute
damageFromHeroesLast6Seconds enemiesHittingMe enemySpellsOnMeLast10 underSmoke'''.split()
        self.assertEqual(list(FIELDS), expected)
        self.assertEqual(len(FIELDS), 54)

    def test_hidden_rejected_at_every_depth(self):
        for path in ('root', 'decision', 'situation', 'nested', 'roles'):
            with self.subTest(path=path):
                context = fixture()
                situation = context['situations'][0]['situation']
                if path == 'root':
                    context['hiddenData'] = None
                elif path == 'decision':
                    context['decision']['scenes'][0]['retrospectiveEpisode']['HiddenData'] = 1
                elif path == 'situation':
                    situation['hiddenEnemy'] = 1
                elif path == 'nested':
                    situation['region'] = {'nested': [{'myHiddenValue': None}]}
                else:
                    context['heroes'][0]['roles'] = [{'hiddenRole': 1}]
                with self.assertRaisesRegex(AuditError, 'Hidden'):
                    project_situated_scene(context, 0)

    def test_unknown_fields_rejected(self):
        for path in ('root', 'snapshot', 'situation', 'hero'):
            context = fixture()
            target = {'root': context, 'snapshot': context['situations'][0],
                      'situation': context['situations'][0]['situation'],
                      'hero': context['heroes'][0]}[path]
            target['newUnreviewedField'] = 1
            with self.subTest(path=path), self.assertRaisesRegex(AuditError, 'Unknown'):
                project_situated_scene(context, 0)

    def test_anchor_mismatch_rejected(self):
        context = fixture()
        context['situations'][0]['anchorSeconds'] = 101
        with self.assertRaisesRegex(AuditError, 'anchor'):
            project_situated_scene(context, 0)

    def test_unknowns_removed_only_for_available_group(self):
        baseline = project_situated_scene(fixture(), 0)['unknowns']
        for field, value, removed in (
                ('healthShare', 0, {'health-mana'}),
                ('teleportReady', False, {'cooldowns', 'inventory-readiness'}),
                ('visibleEnemies', 0, {'visibility'}),
                ('aheadOfOwnWave', 0, {'creep-waves'}),
                ('region', 'river', set()),
                ('alliesAlive', 4, set())):
            with self.subTest(field=field):
                context = fixture()
                context['situations'][0]['situation'][field] = value
                actual = project_situated_scene(context, 0)['unknowns']
                self.assertEqual(actual, [item for item in baseline if item not in removed])
                for item in ('intent', 'safe-path', 'patch-mechanics'):
                    self.assertIn(item, actual)

    def test_null_and_missing_values_keep_unknowns(self):
        for situation in (None, {}, dict.fromkeys(FIELDS)):
            context = fixture()
            context['situations'][0]['situation'] = situation
            original = project_scene(context['decision'], context['decision']['scenes'][0])
            actual = project_situated_scene(context, 0)
            self.assertEqual(actual['unknowns'], original['unknowns'])
            if situation is None:
                self.assertIsNone(actual['facts'][-2]['value'])
            else:
                self.assertIsNone(actual['facts'][-2]['value']['você']['healthShare'])

    def test_facts_and_legacy_projection_unchanged(self):
        context = fixture()
        before = copy.deepcopy(context)
        original = project_scene(context['decision'], context['decision']['scenes'][0])
        actual = project_situated_scene(context, 0)
        self.assertEqual(actual['schemaVersion'], 5)
        self.assertEqual(actual['task'], 'tactical-review-v5')
        situation = actual['facts'][-2]
        self.assertEqual(situation['id'], 'situation')
        self.assertEqual(situation['perspective'], 'team-visible')
        self.assertEqual(situation['sampleSeconds'], 100)
        actual['facts'] = actual['facts'][:-2]
        actual['schemaVersion'] = 1
        actual['task'] = 'tactical-review-v1'
        self.assertEqual(actual, original)
        self.assertEqual(context, before)
        self.assertEqual(project_scene(context['decision'], context['decision']['scenes'][0]), original)

    def test_roster_uses_player_team_even_without_position(self):
        for team in ('dire', 'radiant'):
            context = fixture()
            context['heroes'][0]['team'] = team
            roster = project_situated_scene(context, 0)['facts'][-1]['value']
            self.assertEqual(len(roster), 10)
            for source, projected in zip(context['heroes'], roster):
                self.assertEqual(projected['team'], 'aliado' if source['team'] == team else 'inimigo')
                self.assertEqual(projected['name'], source['name'])
                self.assertEqual(projected['roles'], source['roles'])

    def test_unavailable_catalog_is_empty(self):
        context = fixture()
        context['heroes'] = []
        self.assertEqual(project_situated_scene(context, 0)['facts'][-1]['value'], [])

    def test_patch_unknown_is_not_added_when_verified(self):
        context = fixture()
        context['decision']['patch'].update(matchPatch='test-patch', mechanicsVerified=True)
        self.assertNotIn('patch-mechanics', project_situated_scene(context, 0)['unknowns'])

    def test_prompt_copies_output_contract_and_limits(self):
        self.assertLessEqual(len(SYSTEM_V5.splitlines()), 25)
        self.assertEqual(SYSTEM_V5.split('Contrato de saída', 1)[1], SYSTEM.split('Contrato de saída', 1)[1])
        for line in SYSTEM.splitlines():
            if line.startswith(('opportunity/action até', 'completionCriterion até')):
                self.assertIn(line, SYSTEM_V5.splitlines())


if __name__ == '__main__':
    unittest.main()
