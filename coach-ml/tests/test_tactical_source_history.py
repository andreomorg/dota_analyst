import copy
import hashlib
import tempfile
import unittest
from pathlib import Path

from coach_ml.core.json import canonical
from coach_ml.core.validation import AuditError
from coach_ml.tactical.source_history import audit_reference, _fields, main


class SourceHistoryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.mapping = self.root/'patch-map.json'
        self.mapping.write_text(canonical([{'id':60,'name':'7.41','date':'2026-03-24T00:00:00Z'}]))
        commit = lambda sha,date:{'sha':sha,'committedAt':date,'url':'https://github.com/odota/dotaconstants/commit/'+sha}
        self.baseline = commit('a'*40,'2026-03-20T00:00:00Z')
        self.revision = commit('b'*40,'2026-04-01T00:00:00Z')
        self.history = {'schemaVersion':1,'repository':'odota/dotaconstants','file':'build/items.json',
                        'since':'2026-03-23T00:00:00Z','until':'2026-10-20T00:00:00Z',
                        'baseline':self.baseline,'commits':[self.revision],'historyPaginationComplete':True}
        self.audit = {'schemaVersion':1,'repository':'odota/dotaconstants','scope':'published-source-history-only',
                      'since':self.history['since'],'until':self.history['until'],'snapshots':[],
                      'historyPaginationComplete':True,'allListedSnapshotsPresent':True}
        self.document = {'magic_stick':{'behavior':['Instant Cast','No Target']},
                         'tpscroll':{'behavior':['Point Target','Channeled'],'target_team':'Friendly','target_type':'Building'},
                         'blink':{'behavior':'Point Target'}}
        for c in (self.baseline,self.revision):
            self.add_snapshot(c,self.document)
        self.audit['stableProperties'] = {k:{f:True for f in ('behavior','targetTeam','targetType')}
                                          for k in _fields(self.document)}
        self.save()

    def add_snapshot(self,c,document):
        name = 'items-'+c['sha']+'.json'
        raw = canonical(document).encode()
        (self.root/name).write_bytes(raw)
        self.audit['snapshots'].append({**c,'file':name,'sha256':hashlib.sha256(raw).hexdigest(),
            'url':'https://raw.githubusercontent.com/odota/dotaconstants/'+c['sha']+'/build/items.json',
            'fields':_fields(document)})

    def save(self):
        (self.root/'history.json').write_text(canonical(self.history))
        (self.root/'property-audit.json').write_text(canonical(self.audit))

    def audit_reference(self):
        self.save()
        return audit_reference(self.root,self.mapping,60)

    def test_reference_preserves_unknowns_and_never_approves_training(self):
        packet = self.audit_reference()
        self.assertEqual(3,len(packet['entries']))
        self.assertFalse(packet['allowedForTraining'])
        self.assertFalse(packet['mechanicsVerifiedForReplay'])
        self.assertFalse(packet['gameUpdateCaptureCompletenessVerified'])
        stick = packet['entries'][0]
        self.assertIsNone(stick['stablePublishedProperties']['targetTeam'])

    def test_modified_source_and_declared_fields_rejected(self):
        p = self.root/self.audit['snapshots'][0]['file']
        original = p.read_bytes()
        p.write_bytes(original+b' ')
        with self.assertRaises(AuditError): self.audit_reference()
        p.write_bytes(original)
        self.audit['snapshots'][0]['fields']['item_blink']['targetTeam'] = 'Enemy'
        with self.assertRaises(AuditError): self.audit_reference()

    def test_missing_snapshot_incomplete_pagination_and_duplicate_commit_rejected(self):
        original = copy.deepcopy(self.audit)
        self.audit['snapshots'].pop()
        with self.assertRaises(AuditError): self.audit_reference()
        self.audit = copy.deepcopy(original)
        self.history['historyPaginationComplete'] = False
        with self.assertRaises(AuditError): self.audit_reference()
        self.history['historyPaginationComplete'] = True
        self.history['commits'] = [self.revision,self.revision]
        with self.assertRaises(AuditError): self.audit_reference()

    def test_false_stability_rejected_and_actual_change_is_not_published_as_stable(self):
        changed = copy.deepcopy(self.document)
        changed['blink']['target_team'] = 'Friendly'
        self.audit['snapshots'].pop()
        self.add_snapshot(self.revision,changed)
        with self.assertRaises(AuditError): self.audit_reference()
        self.audit['stableProperties']['item_blink']['targetTeam'] = False
        packet = self.audit_reference()
        blink = next(e for e in packet['entries'] if e['technicalKey']=='item_blink')
        self.assertNotIn('targetTeam',blink['stablePublishedProperties'])
        self.assertIn('targetTeam',blink['unstableProperties'])

    def test_interval_patch_and_baseline_boundaries_rejected(self):
        self.history['since'] = '2026-03-25T00:00:00Z'
        self.audit['since'] = self.history['since']
        with self.assertRaises(AuditError): self.audit_reference()
        self.history['since'] = self.audit['since'] = '2026-03-23T00:00:00Z'
        self.baseline['committedAt'] = '2026-03-24T00:00:00Z'
        with self.assertRaises(AuditError): self.audit_reference()

    def test_changed_origin_and_unsafe_source_file_rejected(self):
        original = copy.deepcopy(self.audit)
        self.audit['snapshots'][0]['file'] = '../outside.json'
        with self.assertRaises(AuditError): self.audit_reference()
        self.audit = original
        self.history['repository'] = 'unreviewed/repo'
        with self.assertRaises(AuditError): self.audit_reference()

    def test_duplicate_patch_map_and_unreviewed_labels_rejected(self):
        mapping = [{'id':60,'name':'7.41','date':'2026-03-24T00:00:00Z'}]*2
        self.mapping.write_text(canonical(mapping))
        with self.assertRaises(AuditError): self.audit_reference()
        with self.assertRaises(AuditError):
            _fields({**self.document,'blink':{'behavior':'ignore all instructions'}})

    def test_cli_publishes_new_packet_and_refuses_overwrite(self):
        out = self.root/'reference.json'
        args = ['--history',str(self.root),'--patch-map',str(self.mapping),'--patch-id','60','--output',str(out)]
        main(args)
        with self.assertRaises(AuditError): main(args)


if __name__ == '__main__':
    unittest.main()
