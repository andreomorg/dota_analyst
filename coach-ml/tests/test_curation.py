import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from coach_ml.audit import AuditError, canonical, iter_json_array
from coach_ml.competencies import INSTRUCTIONS, expected_response, project_request
from coach_ml.curation import deduplicate
from coach_ml.curriculum import file_hash
from test_project import scene


class CurationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source, self.output = self.root / 'source', self.root / 'output'
        self.source.mkdir()
        self.manifest = self.root / 'manifest.json'
        self.manifest.write_text(json.dumps({'matches': [
            {'matchId': match, 'split': split, 'sourceHash': 'a' * 64}
            for match, split in [('100', 'train'), ('101', 'train'), ('200', 'validation-new')]]}))
        (self.source / 'instructions.txt').write_text(INSTRUCTIONS, encoding='utf-8')
        self.write('development', [('100', 10), ('100', 11), ('101', 10), ('101', 12)])
        self.write('validation', [('200', 10), ('200', 13)])

    def write(self, stem, examples):
        rows, metadata = [], []
        for index, (match, hits) in enumerate(examples):
            original = scene(match)
            original['role']['position'] = 1
            original['input']['atOrBeforeAnchor']['economy']['lastHits'] = hits
            request = project_request(original, 'economy-snapshot')
            target = expected_response(request)
            episode = 'episode-' + str(index)
            identity = f'{match}/41/{episode}'
            metadata.append({'row': index, 'matchId': match, 'heroId': 41, 'episodeId': episode,
                'sourceHash': 'a' * 64, 'sourceSceneId': identity,
                'competencyId': 'economy-snapshot', 'exampleId': identity + '/economy-snapshot',
                'request': request, 'target': target, 'provisionalPolicyTarget': True, 'humanReviewed': False})
            rows.append({'messages': [{'role': 'system', 'content': INSTRUCTIONS},
                {'role': 'user', 'content': canonical(request)}, {'role': 'assistant', 'content': canonical(target)}]})
        (self.source / (stem + '.jsonl')).write_text(''.join(canonical(r) + '\n' for r in rows), encoding='utf-8')
        (self.source / (stem + '-metadata.json')).write_text(json.dumps(metadata), encoding='utf-8')

    def build(self):
        return deduplicate(self.source, self.output, self.manifest, 'competency-review-v1')

    def test_removes_internal_and_cross_partition_duplicates_preserving_matches_and_source(self):
        before = {p.name: file_hash(p) for p in self.source.iterdir()}
        result = self.build()
        self.assertEqual(result['removedDuplicateInputs'], {'train': 1, 'validation': 1})
        self.assertEqual(result['examplesAfter'], {'train': 3, 'validation': 1})
        self.assertEqual(result['data']['train']['matches'], 2)
        self.assertEqual(result['data']['validation']['matches'], 1)
        self.assertTrue(result['inputIsolationPassed'])
        self.assertEqual(before, {p.name: file_hash(p) for p in self.source.iterdir()})
        metas = list(iter_json_array(self.output / 'development-metadata.json'))
        self.assertEqual([m['row'] for m in metas], [0, 1, 2])
        self.assertEqual([m['target']['observation']['lastHits'] for m in metas], [10, 11, 12])

    def test_would_not_silently_remove_an_entire_validation_match(self):
        self.write('validation', [('200', 10)])
        with self.assertRaisesRegex(AuditError, 'every example'):
            self.build()
        self.assertFalse((self.output / 'curation-audit.json').exists())
        self.assertFalse((self.output / 'development.jsonl').exists())

    def test_existing_output_is_preserved(self):
        self.output.mkdir()
        sentinel = self.output / 'keep.txt'
        sentinel.write_text('keep')
        with self.assertRaisesRegex(AuditError, 'new directory'):
            self.build()
        self.assertEqual(sentinel.read_text(), 'keep')

    def test_misaligned_source_is_rejected_before_output_creation(self):
        metadata = list(iter_json_array(self.source / 'validation-metadata.json'))
        metadata[0]['target']['exercise'] = 'altered'
        (self.source / 'validation-metadata.json').write_text(json.dumps(metadata))
        with self.assertRaisesRegex(AuditError, 'alignment'):
            self.build()
        self.assertFalse(self.output.exists())

    def test_retained_messages_are_an_exact_subset_and_hashes_cover_outputs(self):
        original = set((self.source / 'development.jsonl').read_text(encoding='utf-8').splitlines())
        result = self.build()
        retained = set((self.output / 'development.jsonl').read_text(encoding='utf-8').splitlines())
        self.assertTrue(retained <= original)
        for name, expected in result['outputFiles'].items():
            self.assertEqual(file_hash(self.output / name), expected)

    def test_source_changed_during_curation_never_publishes_success(self):
        reads = 0

        def changing_hash(path):
            nonlocal reads
            if Path(path) == self.source / 'development.jsonl':
                reads += 1
                if reads > 1:
                    return '0' * 64
            return file_hash(path)

        with patch('coach_ml.curation.file_hash', side_effect=changing_hash):
            with self.assertRaisesRegex(AuditError, 'Source changed'):
                self.build()
        self.assertFalse((self.output / 'curation-audit.json').exists())

    def test_source_cannot_be_used_as_output(self):
        with self.assertRaisesRegex(AuditError, 'new directory'):
            deduplicate(self.source, self.source, self.manifest, 'competency-review-v1')
