import json
from pathlib import Path
import tempfile
import unittest

from coach_ml.audit import AuditError
from coach_ml.predictions import prediction_prefix


class PredictionResumeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / 'predictions.jsonl'
        self.metadata = [{'matchId': '123', 'episodeId': 'death-1', 'heroId': 1,
                          'exampleId': 'ex1', 'competencyId': 'position-coverage'},
                         {'matchId': '124', 'episodeId': 'death-2', 'heroId': 2,
                          'exampleId': 'ex2', 'competencyId': 'position-coverage'}]

    def row(self, index=0):
        return self.metadata[index] | {'row': index, 'variant': 'base', 'text': '{}',
                                       'newTokens': 2, 'hitTokenLimit': False}

    def write(self, rows):
        self.path.write_text(''.join(json.dumps(row) + '\n' for row in rows), encoding='utf-8')

    def read(self):
        return prediction_prefix(self.path, self.metadata, 'base')

    def test_missing_and_empty_files_start_at_zero(self):
        self.assertEqual(0, self.read())
        self.path.touch()
        self.assertEqual(0, self.read())

    def test_partial_and_complete_prefix(self):
        self.write([self.row()])
        self.assertEqual(1, self.read())
        self.write([self.row(), self.row(1)])
        self.assertEqual(2, self.read())

    def test_incomplete_empty_and_damaged_lines_preserved(self):
        for content in (json.dumps(self.row()).encode(), b'{"row":', b'\n',
                        b'\x00\n', b'\xff\n', b'[]\n'):
            with self.subTest(content=content):
                self.path.write_bytes(content)
                with self.assertRaises((AuditError, UnicodeError)):
                    self.read()
                self.assertEqual(content, self.path.read_bytes())

    def test_identity_order_and_types_must_match(self):
        for change in ({'row': True}, {'row': 1}, {'variant': 'adapted'},
                       {'heroId': True}, {'matchId': 123}, {'episodeId': 'other'},
                       {'exampleId': 'other'}, {'competencyId': 'other'}):
            with self.subTest(change=change):
                self.write([self.row() | change])
                with self.assertRaises(AuditError):
                    self.read()

    def test_invalid_generation_fields(self):
        for change in ({'text': None}, {'newTokens': True}, {'newTokens': 0},
                       {'hitTokenLimit': 1}):
            with self.subTest(change=change):
                self.write([self.row() | change])
                with self.assertRaises(AuditError):
                    self.read()

    def test_extra_rows_and_duplicate_json_keys_rejected(self):
        self.write([self.row(), self.row(1), self.row(1)])
        with self.assertRaises(AuditError):
            self.read()
        self.path.write_text('{"row":0,"row":1}\n')
        with self.assertRaises(AuditError):
            self.read()

    def test_row_size_is_bounded(self):
        self.path.write_bytes(b'x' * (1024 * 1024 + 1))
        with self.assertRaises(AuditError):
            self.read()

    def test_broken_symlink_is_not_treated_as_new_file(self):
        try:
            self.path.symlink_to(self.path.parent / 'absent')
        except OSError:
            self.skipTest('Symlinks unavailable')
        with self.assertRaises(AuditError):
            self.read()
