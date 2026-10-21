import copy
import hashlib
import tempfile
import unittest
from pathlib import Path

from coach_ml.core.json import canonical
from coach_ml.core.validation import AuditError
from coach_ml.tactical.actions import add_actions, attach_actions, MASKED_ROLE
from coach_ml.tactical.audit import validate_request
from coach_ml.tactical.pipeline import load_selection
from coach_ml.tactical.projection import project_scene
from coach_ml.tactical.refinement import grounding_notes
from coach_ml.tactical.actions import action_review_schema
from coach_ml.tactical.facts import audit_candidate_facts
from test_tactical import candidate
from test_tactical import fixture


def evidence():
    return {'schemaVersion':1,'heroId':1,'anchorSeconds':100,'windowStartSeconds':70,
            'heroes':[{'heroId':1,'team':0,'heroName':'npc_dota_hero_axe'},
                      {'heroId':2,'team':0,'heroName':'npc_dota_hero_lina'}],
            'itemUses':[{'heroId':1,'seconds':99.125,'name':'blink'}], 'abilityUses':None}


def request():
    return project_scene(*fixture())


class TacticalActionTests(unittest.TestCase):
    def test_precise_clock_and_null_empty_are_preserved(self):
        q = add_actions(request(),evidence())
        validate_request(q)
        value = q['facts'][-1]['value']
        self.assertEqual(99.125,value['itemUses'][0]['seconds'])
        self.assertIsNone(value['abilityUses'])
        source = evidence()
        source['abilityUses'] = []
        self.assertEqual([],add_actions(request(),source)['facts'][-1]['value']['abilityUses'])

    def test_future_stale_nonfinite_actor_and_order_rejected(self):
        for seconds in (100.001,69.999,float('nan'),float('inf'),True):
            bad = evidence()
            bad['itemUses'][0]['seconds'] = seconds
            with self.subTest(seconds=seconds),self.assertRaises(AuditError):
                add_actions(request(),bad)
        for events in ([{'heroId':2,'seconds':99,'name':'blink'}],
                       [{'heroId':1,'seconds':99,'name':'blink'},{'heroId':1,'seconds':98,'name':'blink'}]):
            bad = evidence()
            bad['itemUses'] = events
            with self.assertRaises(AuditError):
                add_actions(request(),bad)

    def test_no_personal_final_or_inconsistent_roster_fields(self):
        for field in ('steamId','final','playerName'):
            bad = evidence()
            bad['heroes'][0][field] = 'private'
            with self.assertRaises(AuditError):
                add_actions(request(),bad)
        for mutate in (lambda e:e['heroes'][0].update(team=1),
                       lambda e:e['heroes'].append(e['heroes'][0]),
                       lambda e:e['heroes'][0].update(heroName='personal name'),
                       lambda e:e['heroes'].reverse(),
                       lambda e:e['itemUses'][0].update(name='UPPERCASE'),
                       lambda e:e['itemUses'][0].update(target=2)):
            bad = evidence()
            mutate(bad)
            with self.assertRaises(AuditError):
                add_actions(request(),bad)

    def test_inferred_role_withheld_and_canonical_audit_enforces_it(self):
        q = request()
        q['role']['isInferred'] = True
        add_actions(q,evidence())
        self.assertEqual(MASKED_ROLE,q['role'])
        validate_request(q)
        q['role']['position'] = 4
        with self.assertRaises(AuditError):
            validate_request(q)

    def test_fact_copies_source_and_audit_rejects_changed_limits(self):
        e = evidence()
        q = add_actions(request(),e)
        e['itemUses'].clear()
        self.assertEqual(1,len(q['facts'][-1]['value']['itemUses']))
        q['facts'][-1]['limitations'] = ['Ready now']
        with self.assertRaises(AuditError):
            validate_request(q)

    def test_default_input_and_closed_partitions_preserved(self):
        q = request()
        validate_request(q)
        self.assertNotIn('recorded-actions',[f['id'] for f in q['facts']])
        with self.assertRaises(AuditError):
            load_selection(Path('.'),partition='validation-diagnostic',actions=Path('.'))
        with self.assertRaises(AuditError):
            load_selection(Path('.'),bundle=Path('.'),actions=Path('.'))

    def test_roster_identity_does_not_require_current_position(self):
        q = request()
        coverage = q['facts'][0]['value']
        coverage.update(samples=[],missingHeroPositions=10,viewerObserved=False,observedAllies=None)
        add_actions(q,evidence())
        notes = grounding_notes(q)
        self.assertFalse(notes['viewerObserved'])
        self.assertEqual([2],notes['knownAllies'])
        self.assertIn('uso registrado',notes['focus'])
        schema = action_review_schema(q)
        self.assertEqual(['undetermined'],schema['properties']['preference']['enum'])
        self.assertNotIn('supported-review',schema['properties']['disposition']['enum'])

    def test_use_reference_and_identical_conditions_are_review_flags(self):
        q = add_actions(request(),evidence())
        c = candidate()
        c['opportunity'] = 'Revise o uso de blink.'
        flags = audit_candidate_facts(q,c)['flags']
        self.assertIn('use-claim-without-action-reference',{f['kind'] for f in flags})
        self.assertIn('identical-alternative-conditions',{f['kind'] for f in flags})
        c['evidenceIds'].append('recorded-actions')
        c['alternatives'][1]['conditions'] = ['Different condition to verify']
        self.assertFalse(audit_candidate_facts(q,c)['flags'])

    def test_receipt_binds_index_data_manifest_and_case_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            index, manifest, data = root/'index.jsonl',root/'manifest.json',root/'actions.jsonl'
            index.write_text('index')
            manifest.write_text('manifest')
            row = {'caseId':'case1','matchId':'123','sourceMatchHash':'a'*64,'parserSchemaVersion':1,'evidence':evidence()}
            data.write_text(canonical(row)+'\n')
            sha = lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
            audit = {'schemaVersion':1,'windowSeconds':30,'developmentOnly':True,'personalOrFinalFieldsExported':False,
                     'rows':1,'matches':1,'sourceManifestSha256':sha(manifest),'dataFileSha256':sha(data),
                     'sourceIndexes':[{'name':index.name,'sha256':sha(index)}]}
            audit_path = root/'export-audit.json'
            audit_path.write_text(canonical(audit))
            record = {k:row[k] for k in ('caseId','matchId','sourceMatchHash')}
            original = request()
            projected = attach_actions([(record,original)],root,root,index)
            self.assertEqual(5,len(original['facts']))
            self.assertEqual(6,len(projected[0][1]['facts']))
            for field in ('caseId','matchId','sourceMatchHash'):
                bad = {**record,field:'different'}
                with self.assertRaises(AuditError):
                    attach_actions([(bad,original)],root,root,index)
            for path in (index,manifest,data):
                saved = path.read_bytes()
                path.write_bytes(saved+b' ')
                with self.assertRaises(AuditError):
                    attach_actions([(record,original)],root,root,index)
                path.write_bytes(saved)


if __name__ == '__main__':
    unittest.main()
