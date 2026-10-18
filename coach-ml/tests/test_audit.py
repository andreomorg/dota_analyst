import copy
import json
from pathlib import Path
import tempfile
import unittest

from coach_ml.audit import AuditError, audit_sft, iter_json_array, parse_json, training_status


class LearningAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.manifest = self.root / 'manifest.json'
        self.dataset = self.root / 'dataset.jsonl'
        self.metadata = self.root / 'metadata.json'
        self.hash = 'a' * 64

    def example(self, row=0, match='100', episode='death-41-40'):
        request = {'heroId': 41, 'role': {'position': 1}, 'evidence': {
            'event': {'kind': 'death', 'anchorSeconds': 30, 'eventSeconds': 40},
            'support': {'change': 'unavailable'},
            'economy': {'sampleSeconds': 0, 'lastHits': 1, 'netWorth': 600}}}
        target = {'observation': {'eventKind': 'death', 'lastHits': 1, 'netWorth': 600,
                  'economySampleSeconds': 0, 'supportChange': 'unavailable'},
                  'reviewFocus': 'survival-review', 'exercise': 'Check available resources.',
                  'limitations': 'Observations do not establish cause.',
                  'evidenceIds': ['event', 'support', 'economy']}
        record = {'messages': [{'role': 'system', 'content': 'Use evidence.'},
                              {'role': 'user', 'content': json.dumps(request)},
                              {'role': 'assistant', 'content': json.dumps(target)}]}
        meta = {'row': row, 'matchId': match, 'heroId': 41, 'episodeId': episode,
                'sourceSceneId': f'{match}/41/{episode}', 'sourceHash': self.hash,
                'request': request, 'target': target, 'humanReviewed': False, 'provisionalPolicyTarget': True}
        return record, meta

    def write(self, pairs, splits=None):
        splits = splits or {'100': 'train', '200': 'reserved-test'}
        self.manifest.write_text(json.dumps({'matches': [
            {'matchId': match, 'sourceHash': self.hash, 'split': split}
            for match, split in splits.items()]}), encoding='utf-8')
        self.dataset.write_text(''.join(json.dumps(r) + '\n' for r, _ in pairs), encoding='utf-8')
        self.metadata.write_text(json.dumps([m for _, m in pairs]), encoding='utf-8')

    def audit(self):
        return audit_sft(self.dataset, self.metadata, self.manifest, 'train')

    def test_valid_example_preserves_provisional_review_without_granting_training_approval(self):
        self.write([self.example()])
        result = self.audit()
        self.assertTrue(result['structuralIntegrityPassed'])
        self.assertEqual(result['rows'], 1)
        self.assertEqual(result['reviewOriginCounts'], {'assistant-provisional': 1})
        self.assertFalse(result['trainingEligibilityGranted'])
        self.assertFalse(result['tacticalQualityApproved'])

    def test_reserved_match_cannot_leak_into_training_even_when_target_is_valid(self):
        self.write([self.example(match='200')])
        with self.assertRaisesRegex(AuditError, 'Partition leakage'):
            self.audit()

    def test_known_validation_is_also_excluded_from_training(self):
        self.write([self.example()], {'100': 'validation-known'})
        with self.assertRaisesRegex(AuditError, 'Partition leakage'):
            self.audit()

    def test_source_hash_must_match_the_frozen_match(self):
        record, meta = self.example()
        meta['sourceHash'] = 'b' * 64
        self.write([(record, meta)])
        with self.assertRaisesRegex(AuditError, 'Source hash mismatch'):
            self.audit()

    def test_duplicate_scene_does_not_count_as_another_example(self):
        self.write([self.example(), self.example(row=1)])
        with self.assertRaisesRegex(AuditError, 'Duplicate source scene'):
            self.audit()

    def test_misaligned_request_and_metadata_are_rejected(self):
        record, meta = self.example()
        meta['request'] = copy.deepcopy(meta['request'])
        meta['request']['heroId'] = 100
        self.write([(record, meta)])
        with self.assertRaisesRegex(AuditError, 'Request alignment'):
            self.audit()

    def test_target_cannot_contradict_input_even_if_metadata_has_the_same_wrong_target(self):
        record, meta = self.example()
        meta['target']['observation']['lastHits'] = 99
        record['messages'][2]['content'] = json.dumps(meta['target'])
        self.write([(record, meta)])
        with self.assertRaisesRegex(AuditError, 'contradicts input'):
            self.audit()

    def test_boolean_is_not_a_valid_numeric_observation(self):
        record, meta = self.example()
        meta['target']['observation']['lastHits'] = True
        record['messages'][2]['content'] = json.dumps(meta['target'])
        self.write([(record, meta)])
        with self.assertRaisesRegex(AuditError, 'contradicts input'):
            self.audit()

    def test_future_economy_is_rejected(self):
        record, meta = self.example()
        meta['request']['evidence']['economy']['sampleSeconds'] = 31
        record['messages'][1]['content'] = json.dumps(meta['request'])
        self.write([(record, meta)])
        with self.assertRaisesRegex(AuditError, 'Future or invalid economy'):
            self.audit()

    def test_unknown_evidence_is_rejected(self):
        record, meta = self.example()
        meta['target']['evidenceIds'].append('invented')
        record['messages'][2]['content'] = json.dumps(meta['target'])
        self.write([(record, meta)])
        with self.assertRaisesRegex(AuditError, 'Invalid evidence'):
            self.audit()

    def test_review_focus_must_correspond_to_the_observed_event_kind(self):
        record, meta = self.example()
        meta['target']['reviewFocus'] = 'fight-participation-review'
        record['messages'][2]['content'] = json.dumps(meta['target'])
        self.write([(record, meta)])
        with self.assertRaisesRegex(AuditError, 'focus contradicts'):
            self.audit()

    def test_extra_dataset_row_cannot_be_silently_ignored(self):
        self.write([self.example()])
        with self.dataset.open('a', encoding='utf-8') as stream:
            stream.write(json.dumps(self.example()[0]) + '\n')
        with self.assertRaisesRegex(AuditError, 'lengths differ'):
            self.audit()

    def test_many_rows_with_one_exercise_warn_without_claiming_tactical_quality(self):
        self.write([self.example(row=i, episode=f'episode-{i}') for i in range(25)])
        report = self.audit()
        self.assertEqual(report['uniqueExercises'], 1)
        self.assertEqual(report['duplicateInputOccurrences'], 24)
        self.assertTrue(any(w.startswith('LOW_EXERCISE_DIVERSITY') for w in report['warnings']))
        self.assertFalse(report['tacticalQualityApproved'])

    def test_metadata_stream_survives_chunk_boundaries_and_unicode(self):
        values = [{'text': 'á }, "]" \\ newline\n' * 500, 'number': 1e30}, {'row': 1}]
        self.metadata.write_text('\ufeff' + json.dumps(values, ensure_ascii=False), encoding='utf-8')
        self.assertEqual(list(iter_json_array(self.metadata, chunk_size=7)), values)

    def test_metadata_rejects_truncation_trailing_comma_and_extra_data(self):
        for text in ['[{"row":', '[{},]', '[{}] {}', '[{}']:
            with self.subTest(text=text):
                self.metadata.write_text(text, encoding='utf-8')
                with self.assertRaises(AuditError):
                    list(iter_json_array(self.metadata, chunk_size=3))

    def test_duplicate_keys_and_nonfinite_values_are_rejected(self):
        for text in ['{"x":1,"x":2}', '{"x":NaN}', '{"x":1e999}']:
            with self.subTest(text=text), self.assertRaises(AuditError):
                parse_json(text)

    def test_status_never_infers_a_live_process_or_improvement_from_a_progress_file(self):
        (self.root / 'live-status.json').write_text(json.dumps({
            'status': 'training', 'cursor': 25, 'totalSamples': 100, 'step': 2, 'totalSteps': 8}))
        report = training_status(self.root)
        self.assertEqual(report['percentCompleted'], 25)
        self.assertFalse(report['processStateVerified'])
        self.assertFalse(report['tacticalImprovementValidated'])
        self.assertFalse(report['trainingResultAvailable'])


if __name__ == '__main__':
    unittest.main()
