import unittest
from unittest.mock import patch

from coach_ml.core.json import canonical
from coach_ml.core.validation import AuditError
from coach_ml.tactical.contracts import validate_candidate
from coach_ml.tactical.curation import validate_critique, DIMENSIONS
from coach_ml.tactical.projection import project_scene
from coach_ml.tactical.pipeline import ollama_generator
from coach_ml.tactical.refinement import grounding_notes
from coach_ml.tactical.structured import candidate_schema, critique_schema
from coach_ml.tactical.facts import audit_candidate_facts
from coach_ml.tactical.support import add_support
from coach_ml.tactical.dataset import apply_content_review
from coach_ml.core.json import digest
import copy
from test_tactical import fixture, candidate


class TacticalCalibrationTests(unittest.TestCase):
    def test_objective_team_is_checked_at_the_claimed_time(self):
        context,scene=fixture()
        event=lambda time,taken: {'kind':0,'seconds':time,'takenBy':taken,'killerHeroId':None,
            'owner':1-taken,'towerTier':1,'lane':0,'meleeBarracks':None,'denied':False,'aegisHeroId':None}
        scene['atOrBeforeAnchor']['recentObjectives']=[event(80,0),event(95,1)]
        request=project_scene(context,scene)
        value=candidate()
        value['opportunity']='Após a torre ser registrada como tomada pelo seu time em 95s, reveja as opções.'
        self.assertEqual(audit_candidate_facts(request,value)['flags'][0]['kind'],'objective-kind-time-or-team-contradiction')
        value['opportunity']='Após a torre ser registrada como tomada pelo inimigo em 95s, reveja as opções.'
        self.assertEqual(audit_candidate_facts(request,value)['flags'],[])
        value['opportunity']='Após a torre ser registrada como tomada pelo seu time em 80s, reveja as opções.'
        self.assertEqual(audit_candidate_facts(request,value)['flags'],[])
        value['opportunity']='Após a torre inimiga ser registrada como tomada pelo inimigo em 95s, reveja as opções.'
        self.assertEqual(audit_candidate_facts(request,value)['flags'][0]['kind'],'objective-owner-contradiction')

    def test_objective_inventory_and_position_assertions_differ_from_questions(self):
        request=project_scene(*fixture())
        value=candidate()
        value['opportunity']='Após a tomada da torre inimiga, reveja a coordenação.'
        value['limitations']=['O jogador 1 possui Shard.','A posição do viewer não está registrada.']
        self.assertEqual({f['kind'] for f in audit_candidate_facts(request,value)['flags']},
                         {'objective-claim-without-record','inventory-possession-without-source','observed-viewer-marked-absent'})
        value['opportunity']='Verifique se uma torre foi tomada antes de escolher um objetivo.'
        value['limitations']=['Compra não prova posse, nem disponibilidade de apoio.']
        self.assertEqual(audit_candidate_facts(request,value)['flags'],[])
        value['opportunity']='Verifique se o jogador 1 possui Shard no inventário do replay.'
        self.assertEqual(audit_candidate_facts(request,value)['flags'],[])

    def test_unavailable_support_does_not_support_asserted_change(self):
        request=project_scene(*fixture())
        sample={'targetSeconds':100,'viewerSampleSeconds':100,'allySampleSeconds':100,'distance':100}
        add_support(request,{'heroId':1,'allyHeroId':2,'anchorSeconds':100,'unavailableReason':'earlier-ally-unobserved',
                             'expectedPairedSeconds':11,'observedPairedSeconds':1,'change':0,'earlier':None,'atAnchor':sample})
        value=candidate()
        value['opportunity']='Após o objetivo, a distância do aliado aumentou.'
        self.assertEqual(audit_candidate_facts(request,value)['flags'][0]['kind'],'support-change-without-endpoints')
        value['opportunity']='Reavalie se a distância do aliado aumentou, recuperando os quadros ausentes.'
        self.assertEqual(audit_candidate_facts(request,value)['flags'],[])

    def test_viewer_is_not_its_own_ally_and_current_position_is_not_earlier_gap(self):
        request=project_scene(*fixture())
        value=candidate()
        value['opportunity']='Compare coordenação com o aliado 1.'
        self.assertEqual(audit_candidate_facts(request,value)['flags'][0]['kind'],'viewer-referenced-as-own-ally')
        value['opportunity']='Compare coordenação com o aliado 2.'
        value['limitations']=['Posição do observador não é confirmada.']
        self.assertEqual(audit_candidate_facts(request,value)['flags'][0]['kind'],'observed-viewer-marked-absent')
        value['limitations']=['Ausência de observação do viewer em 90s impede comparação anterior.']
        self.assertEqual(audit_candidate_facts(request,value)['flags'],[])

    def test_content_review_only_removes_and_is_bound_to_every_input_and_prediction(self):
        request = project_scene(*fixture())
        cases=[({'caseId':'one'},request),({'caseId':'two'},request)]
        predictions=[{'caseId':'one','text':'original'},{'caseId':'two','text':'original'}]
        accepted=[(cases[0][0],request,candidate())]
        rejected=[{'caseId':'two','reason':'critic-reject'}]
        receipt={'schemaVersion':1,'reviewer':'assistant-provisional','humanReviewed':False,
                 'sourcePredictionsSha256':digest(predictions),'decisions':[
                     {'caseId':'one','inputSha256':digest(request),'decision':'veto','reason':'Unsupported inventory claim'},
                     {'caseId':'two','inputSha256':digest(request),'decision':'keep-provisional','reason':'Cannot override rejection'}]}
        kept,removed=apply_content_review(cases,predictions,accepted,rejected,receipt)
        self.assertEqual(kept,[])
        self.assertEqual(len(removed),2)
        for change in ('predictions','input','duplicate'):
            bad=copy.deepcopy(receipt)
            if change=='predictions': bad['sourcePredictionsSha256']='0'*64
            elif change=='input': bad['decisions'][0]['inputSha256']='0'*64
            else: bad['decisions'][1]['caseId']='one'
            with self.assertRaises(AuditError):
                apply_content_review(cases,predictions,accepted,rejected,bad)

    def test_support_after_anchor_and_preference_contradictions_require_review(self):
        request = project_scene(*fixture())
        sample = lambda target,distance: {'targetSeconds':target,'viewerSampleSeconds':target,
                                         'allySampleSeconds':target,'distance':distance}
        add_support(request,{'heroId':1,'allyHeroId':2,'anchorSeconds':100,'unavailableReason':None,
                             'expectedPairedSeconds':11,'observedPairedSeconds':11,'change':2,
                             'earlier':sample(90,200),'atAnchor':sample(100,100)})
        value = candidate()
        value['opportunity'] = 'A separação diminuiu após a âncora.'
        value['preference'] = 'A'
        value['limitations'] = ['Sem recursos confirmados, não há preferência sustentada entre opções.']
        flags = audit_candidate_facts(request,value)['flags']
        self.assertEqual({f['kind'] for f in flags},{'support-window-after-anchor-claim','preference-contradicts-limitation'})
        value['opportunity'] = 'A separação diminuiu antes da âncora.'
        value['preference'] = 'undetermined'
        self.assertEqual(audit_candidate_facts(request,value)['flags'],[])

    def test_grounding_names_only_observed_team_relations(self):
        context, scene = fixture()
        scene['atOrBeforeAnchor']['positions'].append({'heroId':3,'team':1,'sampleSeconds':100,'x':20,'y':30})
        scene['atOrBeforeAnchor']['missingHeroPositions'] = 7
        request = project_scene(context,scene)
        notes = grounding_notes(request)
        self.assertEqual(notes['knownAllies'],[2])
        self.assertEqual(notes['knownEnemies'],[3])
        scene['atOrBeforeAnchor']['positions'] = []
        scene['atOrBeforeAnchor']['missingHeroPositions'] = 10
        notes = grounding_notes(project_scene(context,scene))
        self.assertFalse(notes['viewerObserved'])
        self.assertEqual(notes['knownAllies'],[])
        self.assertEqual(notes['knownEnemies'],[])

    def test_critic_quotes_prose_with_embedded_quotes_not_metadata_ids(self):
        request = project_scene(*fixture())
        value = candidate()
        value['opportunity'] = 'Confira "entrada" com o aliado'
        review = {'scores':{name:1 for name in DIMENSIONS},'criticalFailures':[
                    {'kind':'invented-fact','quote':'Confira "entrada"','reason':'Fixture only','evidenceIds':['positions']}],
                  'verdict':'reject','reason':'Fixture only'}
        validate_critique(request,value,review)
        review['criticalFailures'][0]['quote'] = 'positions'
        with self.assertRaises(AuditError):
            validate_critique(request,value,review)
        quotes = critique_schema(request,value)['properties']['criticalFailures']['items']['properties']['quote']['enum']
        self.assertIn(value['opportunity'],quotes)
        self.assertNotIn('positions',quotes)

    def test_optional_schema_and_examples_are_sent_without_network(self):
        request = project_scene(*fixture())
        calls = []
        class Client:
            def fetch(self,url,payload=None,**kwargs):
                calls.append((url,payload))
                if url.endswith('api/tags'):
                    return {'models':[{'name':'qwen3:8b','digest':'a'*64}]}
                return {'message':{'content':canonical(candidate())},'eval_count':100,
                        'done_reason':'stop','prompt_eval_count':500}
        examples = [{'role':'user','content':'unit fixture'},{'role':'assistant','content':'unit response'}]
        with patch('coach_ml.tactical.pipeline.JsonHttpClient',return_value=Client()):
            identity, generate = ollama_generator('http://fixture/','qwen3:8b',1024,examples=examples,
                                                  schema_factory=candidate_schema,context=12288,thinking=True)
            reply = generate(request)
        self.assertEqual(identity['format'],'json-schema-v1')
        body = calls[-1][1]
        self.assertEqual(body['format']['type'],'object')
        self.assertEqual(body['messages'][1:3],examples)
        self.assertEqual(body['messages'][-1]['content'],canonical(request))
        self.assertEqual(body['options']['num_ctx'],12288)
        self.assertIs(body['think'],True)
        self.assertIs(identity['think'],True)
        self.assertNotIn('thinking',reply)
        validate_candidate(request,__import__('json').loads(reply['text']))


if __name__ == '__main__':
    unittest.main()
