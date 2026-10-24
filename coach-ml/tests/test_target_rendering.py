import json
import unittest
from pathlib import Path

from coach_ml.tactical.policy_v2 import target_for as target_v2
from coach_ml.tactical.policy_v3 import target_for as target_v3
from coach_ml.tactical.situated import project_situated_scene
from test_tactical_policy_v2 import CASES, context
from test_tactical_policy_v3 import request_for


class TargetRenderingTests(unittest.TestCase):
    def test_all_historical_targets_match_the_snapshot_taken_before_extraction(self):
        path = Path(__file__).parent / 'fixtures/targets-v2-v3.json'
        expected = json.loads(path.read_text(encoding='utf-8-sig'))
        actual = []
        for index, values in enumerate([{}, *(values for values, _ in CASES)]):
            actual.append({'v2': target_v2(project_situated_scene(context(values), 0)),
                           'v3': target_v3(request_for(index, values))})
        # Normalize only Python tuple vs JSON array; text, values and list order stay exact.
        self.assertEqual(json.loads(json.dumps(actual)), expected)
