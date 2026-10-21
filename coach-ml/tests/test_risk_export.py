"""Death-risk export with a fake API: no network, no models."""
import csv
import gzip
import json
import tempfile
import unittest
from pathlib import Path

from coach_ml.core.validation import AuditError
from coach_ml.hashing import file_hash
from coach_ml.risk.export import COLUMNS, FEATURE_VERSION, SITUATION_BY_VERSION, export, fetch_with_retry, validate_set


def situation(seconds):
    values = {column: 0 for column in SITUATION_BY_VERSION[FEATURE_VERSION]}
    values.update({'heroId': 1, 'team': 'radiant', 'seconds': seconds, 'role': None, 'roleInferred': False,
                   'region': 'ownJungle', 'onEnemySide': False, 'nearestAllyDistance': None,
                   'secondsSinceLastDeath': None, 'hiddenNearestEnemyDistance': None})
    return values


def row(seconds, dies):
    """A death-risk sample as the API sends it: { situation, diesSoon, diedAtSeconds }."""
    return {'situation': situation(seconds), 'diesSoon': dies, 'diedAtSeconds': seconds + 5 if dies else None}


def sample_set(match_id, rows):
    return {'featureVersion': FEATURE_VERSION, 'matchId': match_id, 'sampleEverySeconds': 10,
            'horizonSeconds': 15, 'samples': rows}


class FakeApi:
    def __init__(self, answers):
        self.answers, self.calls = answers, []

    def __call__(self, match_id):
        self.calls.append(match_id)
        answer = self.answers[match_id]
        return answer.pop(0) if isinstance(answer, list) else answer


MATCHES = [{'matchId': '1', 'split': 'train'}, {'matchId': '2', 'split': 'train'},
           {'matchId': '3', 'split': 'validation-new'}, {'matchId': '4', 'split': 'train'},
           {'matchId': '5', 'split': 'reserved-test'}]


class RiskExportTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.output = Path(self.folder.name) / 'export'

    def tearDown(self):
        self.folder.cleanup()

    def run_export(self, api, partitions=('train', 'validation-new')):
        return export(MATCHES, api, self.output, partitions=list(partitions), sleep=lambda _s: None, log=lambda _m: None)

    def test_writes_one_csv_per_partition_and_a_manifest_that_matches_them(self):
        api = FakeApi({'1': (200, sample_set('1', [row(0, False), row(10, True)]), None),
                       '2': (404, None, None),
                       '3': (200, sample_set('3', [row(0, False)]), None),
                       '4': (422, None, None)})
        summary = self.run_export(api)

        self.assertEqual(summary['skipped'], {'noDocument': ['2'], 'outdated': ['4']})
        self.assertEqual((summary['partitions']['train']['rows'], summary['partitions']['train']['deaths']), (2, 1))
        self.assertEqual(summary['partitions']['train']['deathRate'], 0.5)
        for values in summary['partitions'].values():
            self.assertEqual(file_hash(self.output / values['file']), values['sha256'])
        with gzip.open(self.output / 'train.csv.gz', 'rt', encoding='utf-8', newline='') as stream:
            lines = list(csv.reader(stream))
        self.assertEqual(lines[0], ['matchId', *COLUMNS])
        second = dict(zip(lines[0], lines[2], strict=False))
        self.assertEqual((second['matchId'], second['diesSoon'], second['diedAtSeconds'], second['role']), ('1', '1', '15', ''))
        written = json.loads((self.output / 'export-manifest.json').read_text(encoding='utf-8'))
        self.assertEqual(written, summary)
        self.assertFalse(written['humanReviewed'])

    def test_reserved_partitions_are_refused_before_any_call(self):
        api = FakeApi({})
        with self.assertRaises(AuditError):
            self.run_export(api, partitions=('train', 'reserved-test'))
        self.assertEqual(api.calls, [])
        self.assertFalse(self.output.exists())

    def test_without_a_filter_every_partition_but_the_reserved_is_exported(self):
        api = FakeApi({match_id: (200, sample_set(match_id, []), None) for match_id in ['1', '2', '3', '4']})
        export(MATCHES, api, self.output, sleep=lambda _s: None, log=lambda _m: None)
        self.assertEqual(api.calls, ['1', '2', '3', '4'])

    def test_existing_output_is_refused(self):
        self.output.mkdir()
        with self.assertRaises(AuditError):
            self.run_export(FakeApi({}))

    def test_next_region_rows_are_flattened_with_their_destination(self):
        body = sample_set('1', [{'situation': situation(0), 'regionLater': 'river'},
                                {'situation': situation(10), 'regionLater': None}])
        rows = validate_set(body, '1', kind='next-region')
        self.assertEqual([(row['seconds'], row['regionLater']) for row in rows], [(0, 'river'), (10, None)])
        with self.assertRaises(AuditError):
            validate_set(body, '1')  # a next-region set is not a death-risk one

    def test_unexpected_columns_or_another_match_stop_the_export(self):
        extra = row(0, False) | {'secret': 1}
        hidden_column = row(0, False)
        hidden_column['situation'] = situation(0) | {'secret': 1}
        for answer in [sample_set('1', [extra]), sample_set('1', [hidden_column]), sample_set('9', [row(0, False)]),
                       sample_set('1', [row(0, False)]) | {'featureVersion': FEATURE_VERSION + 1}]:
            with self.subTest(answer=answer), tempfile.TemporaryDirectory() as folder:
                api = FakeApi({'1': (200, answer, None)})
                with self.assertRaises(AuditError):
                    export(MATCHES, api, Path(folder) / 'out', partitions=['train'], sleep=lambda _s: None,
                           log=lambda _m: None)
                self.assertFalse((Path(folder) / 'out' / 'export-manifest.json').exists())

    def test_rate_limit_waits_retry_after_then_continues(self):
        waits = []
        api = FakeApi({'1': [(429, None, 7), (429, None, None), (200, sample_set('1', []), None)]})
        body = fetch_with_retry(api, '1', waits.append)
        self.assertEqual(body['matchId'], '1')
        self.assertEqual(waits, [7, 5])

    def test_wrong_key_stops_at_once(self):
        with self.assertRaises(AuditError):
            fetch_with_retry(FakeApi({'1': (401, None, None)}), '1', lambda _s: None)


if __name__ == '__main__':
    unittest.main()
