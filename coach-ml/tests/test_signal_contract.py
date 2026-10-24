import copy
import json
from pathlib import Path
import unittest

from coach_ml.core.json import canonical
from coach_ml.core.validation import AuditError
from coach_ml.tactical.audit import validate_request
from coach_ml.tactical.policy_v3 import target_for, audit_agreement
from coach_ml.tactical.signal_wording import CITED_FIELDS, details_for
from coach_ml.tactical.situated import project_situated_scene, ROUTE_FIELDS, ROUTE_VALUES
from coach_ml.tactical.prompt_v52 import SYSTEM_V52
from test_tactical_policy_v2 import context


class SignalContractTests(unittest.TestCase):
    def test_domain_fixtures_round_trip_projection_prompt_and_target(self):
        path = Path(__file__).parents[2] / 'DotaAnalyst.Tests/Infrastructure/Fixtures/coaching-signals.json'
        fixtures = json.loads(path.read_text(encoding='utf-8-sig'))
        seen = set()
        for fixture in fixtures:
            with self.subTest(case=fixture['case']):
                source = context(copy.deepcopy(fixture['situation']))
                snapshot = source['situations'][0]
                values = snapshot['situation']
                if any(prefix + suffix in values for prefix in ROUTE_FIELDS.values() for suffix in ROUTE_VALUES.values()):
                    snapshot['escape'] = {}
                    for name, prefix in ROUTE_FIELDS.items():
                        present = any(prefix + suffix in values for suffix in ROUTE_VALUES.values())
                        snapshot['escape'][name] = ({key: values.get(prefix + suffix) for key, suffix in ROUTE_VALUES.items()}
                                                   if present else None)
                        for suffix in ROUTE_VALUES.values():
                            values.pop(prefix + suffix, None)
                snapshot['signals'] = copy.deepcopy(fixture['signals'])
                request = project_situated_scene(source, 0)
                self.assertEqual(next(f['value'] for f in request['facts'] if f['id'] == 'signals'), fixture['signals'])
                self.assertIs(validate_request(request, 'tactical-review-v52'), request)
                rule, target = audit_agreement(request)
                expected_rule = fixture['signals'][0]['id'] if fixture['signals'] else 'R0'
                self.assertEqual(rule, expected_rule)
                seen.add(rule)
                self.assertIn('primeiro sinal', SYSTEM_V52)
                self.assertIn(canonical(fixture['signals']), canonical(request))
                if fixture['signals']:
                    numbers = fixture['signals'][0]['numbers']
                    self.assertEqual(set(numbers), set(CITED_FIELDS[int(rule[1:])-1]))
                    def fmt(field, percent=False):
                        value = numbers.get(field)
                        return 'desconhecido' if value is None else (f'{value * 100:.0f}%' if percent else f'{value:g}')
                    expected_phrase = details_for(fmt)[int(rule[1:])-1][0]
                    self.assertIn(expected_phrase, target['opportunity'])
                source['situations'][0]['situation']['hiddenEnemies'] = 99
                with self.assertRaises(AuditError): project_situated_scene(source, 0)
        self.assertEqual(seen, {f'R{i}' for i in range(12)})

    def test_mutating_only_cited_values_changes_text_without_changing_selected_rule(self):
        from test_tactical_policy_v3 import request_for
        request = request_for(2, {'healthShare': .23, 'visibleEnemiesWithinFightRange': 1})
        original = target_for(request)
        changed = copy.deepcopy(request)
        next(f for f in changed['facts'] if f['id'] == 'signals')['value'][0]['numbers']['healthShare'] = .24
        self.assertEqual(target_for(changed)[0], original[0])
        self.assertIn('24%', target_for(changed)[1]['opportunity'])
        with self.assertRaises(AuditError): audit_agreement(changed)
