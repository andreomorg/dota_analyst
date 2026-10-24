import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from coach_ml.core.validation import AuditError
from coach_ml.tactical.supervision_audit import audit
from coach_ml.tactical.prompt_v5 import SYSTEM_V5
from test_tactical_content_evaluation import case


class SupervisionAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def item(self, index=1):
        request, rule, target = case(index)
        return {'request': request, 'target': target, 'policyRule': rule,
                'labelOrigin': 'deterministic-policy-v2', 'policyVersion': 2}

    def write(self, development, validation=None):
        validation = [self.item(2)] if validation is None else validation
        (self.root / 'config.json').write_text(json.dumps(
            {'profile': 'tactical-review-v5', 'samples': len(development), 'validationSamples': len(validation)}))
        for split, items in (('development', development), ('validation', validation)):
            metadata, records = [], []
            for index, item in enumerate(items):
                metadata.append({**item, 'row': index, 'matchId': f'{split}{index}', 'heroId': item['request']['heroId'],
                                 'episodeId': 'scene', 'exampleId': f'{split}/{index}'})
                records.append({'messages': [{'role': 'system', 'content': item.get('system', SYSTEM_V5)},
                                            {'role': 'user', 'content': json.dumps(item['request'])},
                                            {'role': 'assistant', 'content': json.dumps(item['target'])}]})
            (self.root / (split + '.jsonl')).write_text(''.join(json.dumps(r) + '\n' for r in records))
            (self.root / (split + '-metadata.json')).write_text(json.dumps(metadata))

    def test_valid_examples_reproduce_policy_without_approval(self):
        self.write([self.item()])
        result = audit(self.root)
        self.assertEqual(result['issueCounts'], {})
        self.assertEqual(result['groups'], [])
        self.assertFalse(result['examplesModified'])
        self.assertFalse(result['tacticalQualityApproved'])

    def test_identical_input_duplicates_conflicts_and_cross_partition_leakage(self):
        first = self.item()
        self.write([first, first])
        self.assertEqual(audit(self.root)['groupCounts'], {'duplicate-identical': 1})
        changed = copy.deepcopy(first)
        changed['target']['alternatives'][0]['action'] = 'Outra ação'
        self.write([first, changed], [first])
        result = audit(self.root)
        self.assertEqual(result['groupCounts'], {'cross-partition-input': 1, 'supervision-conflict': 1})
        self.assertEqual(result['issueCounts']['policy-target-mismatch'], 1)

    def test_different_list_order_or_system_is_not_an_exact_conflict(self):
        first = self.item()
        changed = copy.deepcopy(first)
        changed['request']['facts'] = list(reversed(changed['request']['facts']))
        self.write([first, changed])
        self.assertEqual(audit(self.root)['groups'], [])
        changed = copy.deepcopy(first)
        changed['system'] = SYSTEM_V5 + '\nInstrução diferente'
        self.write([first, changed])
        result = audit(self.root)
        self.assertEqual(result['groups'], [])
        self.assertEqual(result['sameRequestWithDifferentVisibleInputs'], 1)
        self.assertEqual(result['issueCounts']['system-prompt-mismatch'], 1)

    def test_unsupported_evidence_hidden_inputs_and_unknown_origin_are_separate(self):
        unsupported, hidden, origin = self.item(), self.item(), self.item()
        unsupported['target']['evidenceIds'] = ['missing-fact']
        hidden['request']['hiddenEnemyCount'] = 3
        origin['labelOrigin'] = 'unknown'
        self.write([unsupported, hidden, origin])
        categories = audit(self.root)['issueCounts']
        for category in ('unsupported-or-invalid-target', 'invalid-source-request', 'non-reproducible-origin'):
            self.assertEqual(categories[category], 1)

    def test_lexical_flags_are_explicitly_ambiguous_not_proven_conflicts(self):
        self.write([self.item()])
        with patch('coach_ml.tactical.supervision_audit.audit_candidate_facts', return_value={'flags': ['review']}):
            result = audit(self.root)
        self.assertEqual(result['issueCounts'], {'ambiguous-content-review': 2})
        self.assertEqual(result['groups'], [])

    def test_metadata_misalignment_and_input_mutation_fail_closed(self):
        self.write([self.item()])
        path = self.root / 'development-metadata.json'
        data = json.loads(path.read_text())
        data[0]['row'] = 2
        path.write_text(json.dumps(data))
        with self.assertRaises(AuditError): audit(self.root)
        self.write([self.item()])
        from coach_ml.tactical.supervision_audit import validate_request
        def mutate(*args):
            (self.root / 'config.json').write_text('{}')
            return validate_request(*args)
        with patch('coach_ml.tactical.supervision_audit.validate_request', side_effect=mutate):
            with self.assertRaisesRegex(AuditError, 'sources changed'): audit(self.root)
