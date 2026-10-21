import copy
import unittest
import tempfile
import contextlib
import io
import json
from pathlib import Path
from unittest.mock import patch

from coach_ml.core.json import canonical
from coach_ml.tactical.bounded import POLICY,V1_POLICY,fact_card,bounded_schema,validate_bounded
from coach_ml.tactical.pipeline import score_generation
from coach_ml.tactical.projection import project_scene
from coach_ml.tactical.support import add_support
from coach_ml.tactical.refinement import main
from coach_ml.core.json import digest
from test_tactical import fixture,candidate


class TacticalBoundedTests(unittest.TestCase):
    def test_missing_earlier_endpoint_recovers_correct_hero_before_anchor(self):
        for reason,heroes in [('earlier-viewer-unobserved',[1]),('earlier-ally-unobserved',[2]),('earlier-both-unobserved',[1,2])]:
            with self.subTest(reason=reason):
                request=project_scene(*fixture())
                add_support(request,{'heroId':1,'allyHeroId':2,'anchorSeconds':100,'unavailableReason':reason,
                    'expectedPairedSeconds':11,'observedPairedSeconds':1,'change':0,'earlier':None,
                    'atAnchor':{'targetSeconds':100,'viewerSampleSeconds':100,'allySampleSeconds':100,'distance':100}})
                card=fact_card(request)
                self.assertEqual(card['recoveryTargets'],[{'heroId':hero,'targetSeconds':90} for hero in heroes])
                self.assertIn('88s e 90s',card['recoveryExercise']['steps'][0])
                value=candidate()
                value.update(opportunity=card['opportunity'],limitations=card['limitations'][:2],exercise=card['recoveryExercise'])
                raw={'text':canonical(value),'stopReason':'stop','boundedFactsPolicy':POLICY}
                self.assertTrue(score_generation(request,raw)['valid'])
                value['exercise']={**value['exercise'],'steps':['Localize a posição atual em 100s.']}
                self.assertFalse(score_generation(request,{**raw,'text':canonical(value)})['valid'])

    def test_missing_viewer_and_initial_window_do_not_fabricate_earlier_time(self):
        context,scene=fixture()
        scene['atOrBeforeAnchor']['positions']=[]
        scene['atOrBeforeAnchor']['missingHeroPositions']=10
        card=fact_card(project_scene(context,scene))
        self.assertEqual(card['recoveryTargets'],[{'heroId':1,'targetSeconds':100}])
        context,scene=fixture()
        source=scene['atOrBeforeAnchor']
        source.update(anchorSeconds=0,economy=None,recentPurchases=[])
        for sample in source['positions']: sample['sampleSeconds']=0
        request=project_scene(context,scene)
        add_support(request,{'heroId':1,'allyHeroId':2,'anchorSeconds':0,'unavailableReason':'insufficient-earlier-window',
            'expectedPairedSeconds':0,'observedPairedSeconds':0,'change':0,'earlier':None,
            'atAnchor':{'targetSeconds':0,'viewerSampleSeconds':0,'allySampleSeconds':0,'distance':100}})
        card=fact_card(request)
        self.assertEqual(card['recoveryTargets'],[])
        self.assertIn('não existe janela anterior válida',card['recoveryExercise']['steps'][0])
        self.assertNotIn('-10',card['recoveryExercise']['steps'][0])

    def test_v1_saved_policy_remains_valid_without_new_recovery_constraint(self):
        context,scene=fixture()
        scene['atOrBeforeAnchor']['positions']=[]
        scene['atOrBeforeAnchor']['missingHeroPositions']=10
        request=project_scene(context,scene)
        card=fact_card(request,V1_POLICY)
        self.assertNotIn('recoveryExercise',card)
        value=candidate()
        value.update(opportunity=card['opportunity'],limitations=card['limitations'][:2],disposition='insufficient',alternatives=[])
        raw={'text':canonical(value),'stopReason':'stop','boundedFactsPolicy':V1_POLICY}
        self.assertTrue(score_generation(request,raw)['valid'])
        self.assertFalse(score_generation(request,{**raw,'boundedFactsPolicy':POLICY})['valid'])

    def test_teacher_cli_keeps_canonical_policy_in_raw_and_experiment_identity(self):
        request=project_scene(*fixture())
        pair=({'caseId':'seed'},request)
        seed={'sourceCaseId':'seed','request':request,'inputSha256':digest(request),
              'authorType':'assistant-provisional','humanReviewed':False,'candidate':candidate()}
        def engine(*args,**kwargs):
            def generate(envelope):
                card=envelope['grounding']['canonicalFacts']
                value=candidate()
                value.update(opportunity=card['opportunity'],limitations=card['limitations'][:2])
                return {'text':canonical(value),'stopReason':'stop','tokens':100}
            return {'engine':'unit-fixture'},generate
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary)
            seeds=root/'seeds.json'
            seeds.write_text(canonical([seed]),encoding='utf8')
            with patch('coach_ml.tactical.refinement.load_cases',return_value=[pair]), \
                 patch('coach_ml.tactical.refinement.load_selection',return_value=[pair]), \
                 patch('coach_ml.tactical.refinement.ollama_generator',side_effect=engine),contextlib.redirect_stdout(io.StringIO()):
                main(['--source',str(root),'--index',str(root/'index'),
                      '--reference-index',str(root/'reference'),'--examples',str(seeds),
                      '--output',str(root/'output'),'--bounded-facts'])
            row=json.loads((root/'output/predictions.jsonl').read_text())
            identity=json.loads((root/'output/identity.json').read_text())
            self.assertTrue(row['valid'])
            self.assertEqual(row['raw']['boundedFactsPolicy'],POLICY)
            self.assertEqual(identity['engine']['boundedFactsPolicy'],POLICY)
            self.assertEqual(identity['engine']['capability'],'conditional-replay-review-only')

    def test_missing_viewer_is_insufficient_not_a_fabricated_location(self):
        context,scene=fixture()
        scene['atOrBeforeAnchor']['positions']=[]
        scene['atOrBeforeAnchor']['missingHeroPositions']=10
        request=project_scene(context,scene)
        card=fact_card(request)
        self.assertEqual(card['dispositions'],['insufficient'])
        self.assertIn('falta uma posição válida',card['opportunity'])
        self.assertEqual(bounded_schema(request)['properties']['preference']['enum'],['undetermined'])

    def test_real_support_projection_controls_direction_and_objective_count(self):
        context,scene=fixture()
        scene['atOrBeforeAnchor']['recentObjectives']=[{'kind':0,'seconds':95,'takenBy':1,
            'killerHeroId':None,'owner':0,'towerTier':1,'lane':0,'meleeBarracks':None,'denied':False,'aegisHeroId':None}]
        request=project_scene(context,scene)
        sample=lambda target,distance: {'targetSeconds':target,'viewerSampleSeconds':target,'allySampleSeconds':target,'distance':distance}
        add_support(request,{'heroId':1,'allyHeroId':2,'anchorSeconds':100,'unavailableReason':None,
            'expectedPairedSeconds':11,'observedPairedSeconds':11,'change':1,'earlier':sample(90,100),'atAnchor':sample(100,200)})
        card=fact_card(request)
        self.assertIn('aumentou',card['opportunity'])
        self.assertNotIn('torre',card['opportunity'])
        self.assertIn('1 evento de objetivo registrado',card['opportunity'])
        self.assertNotIn('após a âncora',card['opportunity'])

    def test_post_validation_rejects_backend_ignoring_schema_and_truncation(self):
        request=project_scene(*fixture())
        card=fact_card(request)
        value=candidate()
        value.update(opportunity=card['opportunity'],limitations=card['limitations'][:2])
        raw={'text':canonical(value),'stopReason':'stop','boundedFactsPolicy':POLICY}
        self.assertTrue(score_generation(request,raw)['valid'])
        for field,bad in [('opportunity','Após a torre tomada, avance.'),
                           ('limitations',['O jogador 1 possui Shard.','Existe uma torre ameaçada.']),
                           ('preference','A')]:
            changed=copy.deepcopy(value)
            changed[field]=bad
            self.assertFalse(score_generation(request,{**raw,'text':canonical(changed)})['valid'])
        self.assertEqual(score_generation(request,{**raw,'stopReason':'length'})['error'],'token-limit')
        self.assertFalse(score_generation(request,{**raw,'boundedFactsPolicy':'unknown'})['valid'])


if __name__=='__main__':
    unittest.main()
