import copy
import hashlib
import tempfile
import unittest
from pathlib import Path

from coach_ml.core.json import canonical
from coach_ml.core.validation import AuditError
from coach_ml.tactical.audit import validate_request
from coach_ml.tactical.patches import attach_patches
from coach_ml.tactical.pipeline import load_selection
from coach_ml.tactical.projection import project_scene
from test_tactical import fixture


class TacticalPatchTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.index = self.root/'index.jsonl'
        self.index.write_text('selection')
        self.manifest = self.root/'manifest.json'
        self.manifest.write_text(canonical({'matches':[{'matchId':'123','sourceHash':'a'*64,'split':'train'}]}))
        self.row = {'matchId':'123','sourceMatchHash':'a'*64,'patchId':60,'source':'opendota:match.patch',
                    'mechanicsVerified':False,'status':200,'url':'https://api.opendota.com/api/matches/123',
                    'retrievedAt':'2026-10-20T22:00:00Z','responseSha256':'b'*64}
        self.q = project_scene(*fixture())
        self.record = {'matchId':'123','sourceMatchHash':'a'*64,'caseId':'case1'}
        self.write()

    def write(self, rows=None):
        data = self.root/'patches.jsonl'
        data.write_text(''.join(canonical(r)+'\n' for r in rows or [self.row]))
        sha = lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
        self.audit = {'schemaVersion':1,'developmentOnly':True,'mechanicsVerified':False,'rows':len(rows or [self.row]),
                      'sourceManifestSha256':sha(self.manifest),'dataFileSha256':sha(data),
                      'sourceIndexes':[{'name':self.index.name,'sha256':sha(self.index)}]}
        (self.root/'export-audit.json').write_text(canonical(self.audit))

    def attach(self):
        return attach_patches([(self.record,self.q)],self.root,self.root,self.index)

    def test_patch_does_not_certify_mechanics_or_mutate_old_input(self):
        old = copy.deepcopy(self.q)
        record,q = self.attach()[0]
        self.assertEqual('60',q['patch']['matchPatch'])
        self.assertFalse(q['patch']['mechanicsVerified'])
        self.assertIn('patch-mechanics',q['unknowns'])
        self.assertEqual(old,self.q)
        self.assertEqual(old['facts'],q['facts'])
        self.assertIn('patchSourceSha256',record)
        validate_request(q)

    def test_missing_stored_metadata_stays_unknown(self):
        self.row = {'matchId':'123','sourceMatchHash':'a'*64,'patchId':None,'source':'stored-match:patch-id',
                    'mechanicsVerified':False,'metadataPresent':False}
        self.write()
        self.assertEqual(self.q,self.attach()[0][1])

    def test_conflicting_or_verified_existing_binding_rejected(self):
        self.q['patch']['matchPatch'] = '59'
        with self.assertRaises(AuditError): self.attach()
        self.q['patch']['matchPatch'] = '60'
        self.q['patch']['mechanicsVerified'] = True
        with self.assertRaises(AuditError): self.attach()

    def test_hash_drift_in_data_manifest_and_index_rejected(self):
        for name in ('patches.jsonl','manifest.json','index.jsonl'):
            p = self.root/name
            original = p.read_bytes()
            p.write_bytes(original+b' ')
            with self.subTest(name=name),self.assertRaises(AuditError): self.attach()
            p.write_bytes(original)

    def test_source_and_partition_identity_cannot_be_rebound_by_receipt(self):
        self.row['sourceMatchHash'] = 'c'*64
        self.write()
        with self.assertRaises(AuditError): self.attach()
        self.row['sourceMatchHash'] = 'a'*64
        self.manifest.write_text(canonical({'matches':[{'matchId':'123','sourceHash':'a'*64,'split':'reserved-test'}]}))
        self.write()
        with self.assertRaises(AuditError): self.attach()

    def test_extra_private_fields_invalid_origin_and_bad_patch_rejected(self):
        valid = copy.deepcopy(self.row)
        mutations = [lambda r:r.update(playerName='private'),lambda r:r.update(patchId=True),
                     lambda r:r.update(patchId=-1),lambda r:r.update(patchId='60'),
                     lambda r:r.update(patchId=2147483648),
                     lambda r:r.update(mechanicsVerified=True),lambda r:r.update(url='https://invalid/'),
                     lambda r:r.update(retrievedAt='invalidZ'),lambda r:r.update(status=True)]
        for mutate in mutations:
            self.row = copy.deepcopy(valid)
            mutate(self.row)
            self.write()
            with self.assertRaises(AuditError): self.attach()

    def test_missing_and_duplicate_matches_rejected(self):
        self.write([self.row,self.row])
        with self.assertRaises(AuditError): self.attach()
        self.write()
        self.record['matchId'] = '124'
        with self.assertRaises(AuditError): self.attach()

    def test_closed_partitions_and_control_bundles_rejected_before_read(self):
        with self.assertRaises(AuditError):
            load_selection(self.root,partition='validation-diagnostic',patches=self.root)
        with self.assertRaises(AuditError):
            load_selection(self.root,bundle=self.root,patches=self.root)


if __name__ == '__main__':
    unittest.main()
