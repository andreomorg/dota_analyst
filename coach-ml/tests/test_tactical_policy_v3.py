import copy
import unittest

from coach_ml.core.validation import AuditError
from coach_ml.tactical.audit import validate_request
from coach_ml.tactical.policy_v2 import target_for as target_v2
from coach_ml.tactical.policy_v3 import target_for, audit_agreement, legacy_request, NAMES
from coach_ml.tactical.signal_wording import CITED_FIELDS
from coach_ml.tactical.situated import project_situated_scene, ROUTE_FIELDS, ROUTE_VALUES
from coach_ml.tactical.prompt_v52 import system_for, SYSTEM_V52
from test_tactical_policy_v2 import CASES, context


def signal(index, values):
    return {'id': f'R{index}', 'name': NAMES[index - 1],
            'numbers': {field: values.get(field) for field in CITED_FIELDS[index - 1]}}


def request_for(index, values):
    source = context(copy.deepcopy(values))
    snapshot = source['situations'][0]
    if any(prefix + suffix in values for prefix in ROUTE_FIELDS.values() for suffix in ROUTE_VALUES.values()):
        # Real C# contexts carry complete route records, not a partial flat route group.
        snapshot['escape'] = {}
        for name, prefix in ROUTE_FIELDS.items():
            present = any(prefix + suffix in values for suffix in ROUTE_VALUES.values())
            snapshot['escape'][name] = ({key: values.get(prefix + suffix) for key, suffix in ROUTE_VALUES.items()}
                                        if present else None)
            if present:
                snapshot['escape'][name]['pathLength'] = values.get(prefix + 'PathLength', 1000)
            for suffix in ROUTE_VALUES.values():
                snapshot['situation'].pop(prefix + suffix, None)
    source['situations'][0]['signals'] = [signal(index, values)] if index else []
    return project_situated_scene(source, 0)


class SignalPolicyTests(unittest.TestCase):
    def test_all_rules_reproduce_historical_targets_and_round_trip(self):
        for index, (values, _) in enumerate(CASES, 1):
            with self.subTest(rule=index):
                request = request_for(index, values)
                legacy = legacy_request(request)
                self.assertEqual(target_v2(legacy), target_for(request))
                self.assertEqual(audit_agreement(request), target_for(request))
                self.assertIs(validate_request(request, 'tactical-review-v52'), request)

    def test_empty_signals_reproduce_insufficient(self):
        request = request_for(0, {})
        self.assertEqual(audit_agreement(request), target_v2(project_situated_scene(context(), 0)))

    def test_policy_reads_only_signal_values_and_audit_detects_disagreement(self):
        request = request_for(1, CASES[0][0])
        request['facts'][-1]['value'][0]['numbers']['visibleEnemiesWithinFightRange'] = 123
        self.assertIn('123', target_for(request)[1]['opportunity'])
        with self.assertRaises(AuditError):
            audit_agreement(request)
        request = request_for(0, CASES[0][0])
        self.assertEqual(target_for(request)[0], 'R0')
        with self.assertRaises(AuditError):
            audit_agreement(request)

    def test_unknown_cited_number_is_preserved(self):
        request = request_for(2, {'healthShare': .2, 'visibleEnemiesWithinFightRange': 1})
        self.assertIn('desconhecido', target_for(request)[1]['opportunity'])
        self.assertEqual(audit_agreement(request)[0], 'R2')

    def test_bad_signals_are_rejected(self):
        original = request_for(1, CASES[0][0])
        for values in (None, [signal(2, {}), signal(1, {})], [signal(1, {}), signal(1, {})],
                       [{'id': 'R1', 'name': NAMES[0], 'numbers': {}}],
                       [signal(1, {'alliesWithinFightRange': float('nan')})]):
            request = copy.deepcopy(original)
            request['facts'][-1]['value'] = values
            with self.assertRaises(AuditError):
                target_for(request)

    def test_legacy_projection_is_unchanged_and_prompt_is_versioned(self):
        source = context(CASES[0][0])
        before = project_situated_scene(source, 0)
        current = copy.deepcopy(source)
        current['situations'][0]['signals'] = [signal(1, CASES[0][0])]
        project_situated_scene(current, 0)
        self.assertEqual(before, project_situated_scene(source, 0))
        self.assertEqual(before['task'], 'tactical-review-v5')
        self.assertEqual(system_for('tactical-review-v52', 'legacy'), SYSTEM_V52)
