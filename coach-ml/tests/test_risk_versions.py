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


def write_v1_export(folder):
    """A version-1 export as the first exporter wrote it: one partition, manifest without 'kind'."""
    columns = ['matchId', *columns_of(1)]
    rows = []
    for index in range(20):
        row = {column: '0' for column in columns}
        row.update({'matchId': '1', 'heroId': str(1 + index % 10), 'team': 'radiant' if index % 10 < 5 else 'dire',
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
