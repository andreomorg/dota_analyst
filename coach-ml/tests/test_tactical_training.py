import copy
import json
from pathlib import Path
import tempfile
import unittest

from coach_ml.audit import audit_sft
from coach_ml.core.json import canonical
from coach_ml.core.validation import AuditError
from coach_ml.evaluation import evaluate
from coach_ml.experiments.preparation import inspect_experiment
from coach_ml.experiments.capabilities import experiment_capability
from coach_ml.tactical.pipeline import SYSTEM
from coach_ml.tactical.projection import project_scene
from coach_ml.tactical.audit import validate_request
from coach_ml.tactical.support import add_support
from coach_ml.tactical.assembly import assemble
from test_tactical import fixture, candidate


def write_case(root, stem, match='1', hero=1):
    context, scene = fixture()
    context['heroId'] = hero
    request, target = project_scene(context,scene), candidate()
    meta = {'row':0,'matchId':match,'heroId':hero,'episodeId':'scene', 'sourceHash':'a'*64,
            'provenance':{'matchId':match,'heroId':hero,'sceneId':'scene','sourceMatchHash':'a'*64},
            'sourceSceneId':f'{match}/{hero}/scene','exampleId':f'{match}/{hero}/scene/tactical-review-v1',
            'request':request,'target':target,'humanReviewed':False,'tacticalQualityApproved':False,
            'labelOrigin':'model-generated-and-model-critiqued','provisionalAccepted':True,
            'sourcePredictionsSha256':'b'*64,'criticIdentitySha256':'c'*64}
    record = {'messages':[{'role':'system','content':SYSTEM},
                          {'role':'user','content':canonical(request)},
                          {'role':'assistant','content':canonical(target)}]}
    (root/(stem+'.jsonl')).write_text(canonical(record)+'\n')
    (root/(stem+'-metadata.json')).write_text(canonical([meta]))
    return record, meta


class TacticalTrainingTests(unittest.TestCase):
    def test_v52_assembly_and_inspection_need_policy_provenance_without_fake_critic(self):
        from coach_ml.tactical.prompt_v52 import SYSTEM_V52
        from coach_ml.tactical.policy_v3 import target_for
        from test_tactical_policy_v2 import context, CASES
        from test_tactical_policy_v3 import signal
        from coach_ml.tactical.situated import project_situated_scene
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            directories = []
            for name, match, anchor in [('train', '1', 100), ('validation', '2', 110)]:
                directory = root / name
                directory.mkdir()
                record, meta = write_case(directory, 'source', match)
                source = context(CASES[0][0])
                source['decision']['scenes'][0]['atOrBeforeAnchor']['anchorSeconds'] = anchor
                source['situations'][0].update(anchorSeconds=anchor, signals=[signal(1, CASES[0][0])])
                request = project_situated_scene(source, 0)
                rule, target = target_for(request)
                meta.update(exampleId=f'{match}/1/scene/tactical-review-v52', request=request, target=target,
                            labelOrigin='deterministic-policy-v3', policyVersion=3, policyRule=rule)
                meta.pop('sourcePredictionsSha256'); meta.pop('criticIdentitySha256')
                for message, content in zip(record['messages'], [SYSTEM_V52, canonical(request), canonical(target)]):
                    message['content'] = content
                (directory / 'provisional-sft.jsonl').write_text(canonical(record) + '\n')
                (directory / 'metadata.json').write_text(canonical([meta]))
                (directory / 'curation-audit.json').write_text(canonical({
                    'profile': 'tactical-review-v52', 'experimentalDataReady': True, 'provisionalAccepted': 1,
                    'humanReviewed': False, 'tacticalQualityApproved': False,
                    'labelOrigin': 'deterministic-policy-v3', 'policyVersion': 3}))
                directories.append(directory)
            cfg = json.loads((Path(__file__).parents[1] / 'configs/lora.example.json').read_text())
            cfg.update(profile='tactical-review-v52', samples=1, validationSamples=1)
            (root / 'config.json').write_text(canonical(cfg))
            manifest = root / 'manifest.json'
            manifest.write_text(canonical({'matches': [{'matchId': match, 'sourceHash': 'a' * 64, 'split': split}
                for match, split in [('1', 'train'), ('2', 'validation-new')]]}))
            cache = root / 'cache'
            (cache / 'hub' / ('models--' + cfg['model'].replace('/', '--')) / 'snapshots' / cfg['revision']).mkdir(parents=True)
            experiment = root / 'experiment'
            assembled = assemble(*directories, root / 'config.json', manifest, experiment,
                                 minimum_train=1, minimum_validation=1)
            self.assertTrue(assembled['inputIsolationPassed'])
            self.assertEqual((experiment / 'instructions.txt').read_text(), SYSTEM_V52)
            inspected = inspect_experiment(experiment, cache, manifest)
            self.assertFalse(inspected['gpuInitialized'])
            self.assertEqual(inspected['profile'], 'tactical-review-v52')

    def test_support_provenance_cannot_carry_extra_future_outcome_fields(self):
        request = project_scene(*fixture())
        support = {'heroId':1,'anchorSeconds':100,'allyHeroId':2,'expectedPairedSeconds':11,'observedPairedSeconds':6,
                   'change':2,'unavailableReason':None,
                   'earlier':{'targetSeconds':90,'viewerSampleSeconds':90,'allySampleSeconds':90,'distance':500.0},
                   'atAnchor':{'targetSeconds':100,'viewerSampleSeconds':99,'allySampleSeconds':100,'distance':300.0}}
        enriched = add_support(request,support)
        validate_request(enriched)
        enriched['facts'][-1]['value']['futureOutcome'] = 'death'
        with self.assertRaises(AuditError):
            validate_request(enriched)

    def test_new_profile_checks_counts_origins_and_reserved_partition_without_ml(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            record, meta = write_case(root,'development')
            manifest = root/'manifest.json'
            manifest.write_text(canonical({'matches':[{'matchId':'1','sourceHash':'a'*64,'split':'train'}]}))
            args = (root/'development.jsonl',root/'development-metadata.json',manifest,'train','tactical-review-v1')
            report = audit_sft(*args)
            self.assertEqual(report['rows'],1)
            self.assertFalse(report['tacticalQualityApproved'])
            self.assertFalse(report['sourceFactsRecomputed'])
            meta['humanReviewed'] = True
            (root/'development-metadata.json').write_text(canonical([meta]))
            with self.assertRaises(AuditError):
                audit_sft(*args)
            meta['humanReviewed'] = False
            meta['request']['facts'][0]['value']['observedAllies'] = 4
            record['messages'][1]['content'] = canonical(meta['request'])
            (root/'development.jsonl').write_text(canonical(record)+'\n')
            (root/'development-metadata.json').write_text(canonical([meta]))
            with self.assertRaises(AuditError):
                audit_sft(*args)
            write_case(root,'development')
            manifest.write_text(canonical({'matches':[{'matchId':'1','sourceHash':'a'*64,'split':'reserved-test'}]}))
            with self.assertRaises(AuditError):
                audit_sft(*args)

    def test_experiment_inspection_and_capability_accept_tactical_profile(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            curved = []
            for name, match, hero in [('train','1',1),('validation','2',2)]:
                directory = root/name
                directory.mkdir()
                record, meta = write_case(directory,'source',match,hero)
                (directory/'provisional-sft.jsonl').write_text(canonical(record)+'\n')
                (directory/'metadata.json').write_text(canonical([meta]))
                (directory/'curation-audit.json').write_text(canonical({'profile':'tactical-review-v1',
                    'experimentalDataReady':True,'provisionalAccepted':1,'humanReviewed':False,'tacticalQualityApproved':False,
                    'sourcePredictionsSha256':'b'*64,'criticIdentitySha256':'c'*64}))
                curved.append(directory)
            cfg = json.loads((Path(__file__).parents[1]/'configs/lora.example.json').read_text())
            cfg.update(profile='tactical-review-v1',samples=1,validationSamples=1)
            (root/'config.json').write_text(canonical(cfg))
            manifest = root/'manifest.json'
            manifest.write_text(canonical({'matches':[{'matchId':m,'sourceHash':'a'*64,'split':s}
                                                       for m,s in [('1','train'),('2','validation-new')]]}))
            cache = root/'cache'
            (cache/'hub'/('models--'+cfg['model'].replace('/','--'))/'snapshots'/cfg['revision']).mkdir(parents=True)
            experiment = root/'experiment'
            assemble(*curved,root/'config.json',manifest,experiment,minimum_train=1,minimum_validation=1)
            report = inspect_experiment(experiment,cache,manifest)
            self.assertTrue(report['inputIsolationPassed'])
            self.assertFalse(report['gpuInitialized'])
            capability = experiment_capability(experiment,cfg)
            self.assertEqual(capability['evaluator'],'tactical-contract-and-lexical-v1')
            with self.assertRaises(AuditError):
                assemble(*curved,root/'config.json',manifest,root/'too-small',minimum_train=2,minimum_validation=1)
            (experiment/'instructions.txt').write_text('changed')
            with self.assertRaises(AuditError):
                inspect_experiment(experiment,cache,manifest)
            (curved[0]/'assistant-review-veto.json').write_text(canonical({'allowedForTraining':False}))
            with self.assertRaises(AuditError):
                assemble(*curved,root/'config.json',manifest,root/'vetoed',minimum_train=1,minimum_validation=1)

    def test_post_training_evaluator_reports_contract_gain_without_tactical_approval(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            _, meta = write_case(root,'validation')
            (root/'config.json').write_text(canonical({'profile':'tactical-review-v1'}))
            (root/'output').mkdir()
            for variant in ('base','adapted'):
                prediction = {key:meta[key] for key in ('row','matchId','heroId','episodeId','exampleId')}
                prediction.update(variant=variant,text=canonical(meta['target']) if variant=='adapted' else 'free prose',
                                  hitTokenLimit=False,newTokens=100)
                (root/'output'/f'predictions-{variant}.jsonl').write_text(canonical(prediction)+'\n')
            report = evaluate(root)
            self.assertEqual(report['pairedContract']['gainedContract'],1)
            self.assertFalse(report['tacticalQualityApproved'])
            self.assertEqual(report['summary'][1]['strictValid'],1)
            with self.assertRaises(AuditError):
                evaluate(root)


if __name__ == '__main__':
    unittest.main()
