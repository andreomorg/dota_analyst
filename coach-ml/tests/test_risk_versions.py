"""Older exports stay readable: a version-1 export (no kind in the manifest, flat rows) loads with a version-1 model's
columns, and version 1 never had a next-region export (needs numpy: the risk extra)."""
import csv
import gzip
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

from coach_ml.core.validation import AuditError
from coach_ml.hashing import file_hash
from coach_ml.risk.export import columns_of

HAS_NUMPY = importlib.util.find_spec('numpy') is not None


def write_v1_export(folder, matches=1):
    """A version-1 export as the first exporter wrote it: one partition, manifest without 'kind'."""
    columns = ['matchId', *columns_of(1)]
    rows = []
    for index in range(20 * matches):
        row = {column: '0' for column in columns}
        row.update({'matchId': str(1 + index // 20), 'heroId': str(1 + index % 10), 'team': 'radiant' if index % 10 < 5 else 'dire',
                    'seconds': str(index * 10), 'role': '3', 'roleInferred': '1', 'region': 'ownJungle',
                    'secondsSinceLastDeath': '', 'diesSoon': '1' if index % 7 == 0 else '0',
                    'diedAtSeconds': str(index * 10 + 5) if index % 7 == 0 else ''})
        rows.append(row)
    path = folder / 'train.csv.gz'
    with gzip.open(path, 'wt', encoding='utf-8', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=columns)
        writer.writeheader()
        writer.writerows(rows)
    manifest = {'featureVersion': 1, 'partitions': {'train': {'file': path.name, 'sha256': file_hash(path), 'rows': len(rows)}}}
    (folder / 'export-manifest.json').write_text(json.dumps(manifest), encoding='utf-8')


class VersionTests(unittest.TestCase):
    def test_version_2_and_3_exports_remain_independently_readable(self):
        from coach_ml.risk.data import read_export
        for version in (2, 3):
            with self.subTest(version=version), tempfile.TemporaryDirectory() as name:
                root = Path(name)
                path = root / 'train.csv.gz'
                columns = ['matchId', *columns_of(version)]
                with gzip.open(path, 'wt', encoding='utf-8', newline='') as stream:
                    writer = csv.DictWriter(stream, fieldnames=columns)
                    writer.writeheader()
                    writer.writerow({column: '0' for column in columns})
                manifest = {'featureVersion': version, 'kind': 'death-risk', 'partitions': {
                    'train': {'file': path.name, 'sha256': file_hash(path), 'rows': 1}}}
                (root / 'export-manifest.json').write_text(json.dumps(manifest))
                self.assertEqual(read_export(root)['featureVersion'], version)

    def test_v3_keeps_v2_columns_but_has_separate_version(self):
        from coach_ml.risk.export import FEATURE_VERSION, SITUATION_BY_VERSION
        from coach_ml.risk.data import VISIBLE_BY_VERSION, HIDDEN_BY_VERSION
        self.assertEqual(FEATURE_VERSION, 3)
        self.assertEqual(SITUATION_BY_VERSION[3], SITUATION_BY_VERSION[2])
        self.assertIsNot(SITUATION_BY_VERSION[3], SITUATION_BY_VERSION[2])
        self.assertEqual(VISIBLE_BY_VERSION[3], VISIBLE_BY_VERSION[2])
        self.assertEqual(HIDDEN_BY_VERSION[3], HIDDEN_BY_VERSION[2])
        self.assertEqual(columns_of(3, 'next-region'), columns_of(2, 'next-region'))

    @unittest.skipUnless(importlib.util.find_spec('sklearn') is not None, 'needs the risk extra (scikit-learn)')
    def test_version_1_still_trains_and_evaluates_four_models(self):
        from coach_ml.risk.data import VISIBLE_BY_VERSION
        from coach_ml.risk.evaluate import evaluate
        from coach_ml.risk.train import train

        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            source = root / 'export'
            source.mkdir()
            write_v1_export(source, matches=20)
            training = train(source, root / 'models', iterations=[1], log=lambda _m: None)
            self.assertEqual(set(training['columns']),
                             {'constant', 'logistic', 'boostedVisible', 'boostedOmniscient'})
            self.assertEqual(training['columns']['boostedVisible'], VISIBLE_BY_VERSION[1])
            report = evaluate(root / 'models', source, root / 'report', partitions=['train'])
            self.assertEqual(set(report['models']), set(training['columns']))

    def test_version_1_never_had_a_next_region_export(self):
        with self.assertRaises(AuditError):
            columns_of(1, 'next-region')
        self.assertEqual(columns_of(2, 'next-region')[-1], 'regionLater')

    @unittest.skipUnless(HAS_NUMPY, 'needs the risk extra (numpy)')
    def test_a_version_1_export_loads_with_a_version_1_models_columns(self):
        import numpy as np

        from coach_ml.risk.data import VISIBLE_BY_VERSION, feature_version, load

        with tempfile.TemporaryDirectory() as name:
            folder = Path(name)
            write_v1_export(folder)
            features, labels, groups, extra = load(folder, ['train'], VISIBLE_BY_VERSION[1], np)

            self.assertEqual(feature_version(folder), 1)
            self.assertEqual(features.shape, (20, len(VISIBLE_BY_VERSION[1])))
            self.assertEqual(int(labels.sum()), 3)
            self.assertEqual(set(groups.tolist()), {1})
            # Version 1's inferred role looked at 10:00: still hidden before then (no roleConfidence column).
            role = VISIBLE_BY_VERSION[1].index('role')
            self.assertTrue(np.isnan(features[0, role]))
            self.assertEqual(extra['diedAtSeconds'][0], 5)

    @unittest.skipUnless(HAS_NUMPY, 'needs the risk extra (numpy)')
    def test_a_version_2_column_is_refused_on_a_version_1_export(self):
        import numpy as np

        from coach_ml.risk.data import load

        with tempfile.TemporaryDirectory() as name:
            folder = Path(name)
            write_v1_export(folder)
            with self.assertRaises(AuditError):
                load(folder, ['train'], ['health'], np)


if __name__ == '__main__':
    unittest.main()
