import copy
import unittest
from pathlib import Path
import tempfile

from coach_ml.core.validation import AuditError
from coach_ml.tactical.projection import project_scene
from coach_ml.tactical.contracts import validate_candidate
from coach_ml.tactical.pipeline import run_cases, score_generation, load_cases
import hashlib
from coach_ml.core.json import canonical
from coach_ml.tactical.curation import validate_critique, DIMENSIONS
from coach_ml.tactical.support import add_support
from coach_ml.tactical.huggingface import trim_tokens
from coach_ml.tactical.dataset import select_provisional
from coach_ml.core.json import digest
from coach_ml.tactical.facts import audit_candidate_facts
from coach_ml.tactical.bundles import load_bundle


def fixture():
    context = {'heroId': 1, 'matchId': 'hidden-match',
               'role': {'position': 1, 'source': 'replay-role-queue', 'isInferred': False},
               'patch': {'matchPatch': None, 'source': 'unknown', 'trainingPatch': None, 'mechanicsVerified': False}}
    scene = {'retrospectiveEpisode': {'id': 'death-secret', 'eventSeconds': 110, 'economy': {'netWorthChange': -100}},
             'atOrBeforeAnchor': {'anchorSeconds': 100, 'positions': [
                 {'heroId': 1, 'team': 0, 'sampleSeconds': 99, 'x': 0, 'y': 0},
                 {'heroId': 2, 'team': 0, 'sampleSeconds': 100, 'x': 10, 'y': 20}],
                 'missingHeroPositions': 8, 'economy': {'sampleSeconds': 60, 'netWorth': 500, 'lastHits': 1},
                 'recentPurchases': [{'seconds': 95, 'heroId': 1, 'itemKey': 'a'},
                                     {'seconds': 95, 'heroId': 1, 'itemKey': 'b'}],
                 'recentAbilityUpgrades': [], 'recentObjectives': None}}
    return context, scene


def candidate():
    return {'schemaVersion': 1, 'disposition': 'conditional-review', 'opportunity': 'Compare options',
            'evidenceIds': ['positions'], 'alternatives': [
                {'id': identifier, 'action': action, 'conditions': ['Check visibility'],
                 'evidenceIds': ['positions'], 'unknowns': ['visibility']}
                for identifier, action in [('A', 'Group'), ('B', 'Hold')]],
            'preference': 'undetermined', 'exercise': {'steps': ['Check anchor'],
            'completionCriterion': 'Record conditions', 'evidenceIds': ['positions']},
            'limitations': ['Not player vision']}


class TacticalTests(unittest.TestCase):
    def test_control_bundle_is_reprojected_and_modified_prompt_is_rejected(self):
        context, scene = fixture()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, bundle_dir = root/'source', root/'bundle'
            (source/'archive').mkdir(parents=True)
            bundle_dir.mkdir()
            raw = b'unit fixture only'
            source_hash = hashlib.sha256(raw).hexdigest()
            (source/'archive'/(source_hash+'.json')).write_bytes(raw)
            (source/'manifest.json').write_text(canonical({'matches':[{'matchId':'1','sourceHash':source_hash,'split':'train'}]}))
            row = {**context,'matchId':'1','controlId':'control-1-100','kind':'control-window',
                   'sourceSplit':'train','sourceHash':source_hash,'atOrBeforeAnchor':scene['atOrBeforeAnchor']}
            controls = bundle_dir/'controls.jsonl'
            controls.write_text(canonical(row)+'\n')
            receipt = {'rows':1,'controlFileSha256':hashlib.sha256(controls.read_bytes()).hexdigest(),
                       'sourceManifestSha256':hashlib.sha256((source/'manifest.json').read_bytes()).hexdigest()}
            (bundle_dir/'control-audit.json').write_text(canonical(receipt))
            (bundle_dir/'independent-audit.json').write_text(canonical({'status':'passed','rows':1}))
            request = project_scene(row,row)
            item = {'matchId':'1','controlId':row['controlId'],'request':request,'inputSha256':digest(request)}
            bundle = bundle_dir/'projected-inputs.jsonl'
            bundle.write_text(canonical(item)+'\n')
            cases = load_bundle(source,bundle)
            self.assertEqual(cases[0][0]['sourceKind'],'control-window')
            self.assertNotIn('retrospectiveEpisode',cases[0][1])
            item['request']['facts'][0]['value']['observedAllies'] = 4
            bundle.write_text(canonical(item)+'\n')
            with self.assertRaises(AuditError):
                load_bundle(source,bundle)

    def test_observed_ally_called_enemy_is_flagged_even_if_schema_is_valid(self):
        request = project_scene(*fixture())
        value = candidate()
        value['opportunity'] = 'Avaliar um ataque ao inimigo 2'
        validate_candidate(request, value)
        flags = audit_candidate_facts(request, value)['flags']
        self.assertEqual(flags[0]['kind'], 'potential-team-contradiction')
        self.assertEqual(flags[0]['heroId'], 2)
        value['opportunity'] = 'Conferir 2 aliados observados'
        self.assertEqual(audit_candidate_facts(request, value)['flags'][0]['expected'], 1)
        self.assertFalse(audit_candidate_facts(request, candidate())['flags'])
        value['opportunity'] = 'Revise na âncora 101s'
        self.assertEqual(audit_candidate_facts(request,value)['flags'][0]['kind'],'anchor-time-contradiction')

    def test_curation_rejects_changed_source_artifact_and_does_not_accept_critical_failure(self):
        request = project_scene(*fixture())
        raw = {'text': canonical(candidate()), 'stopReason': 'stop'}
        prediction = {'caseId': 'one', 'inputSha256': digest(request), 'raw': raw, **score_generation(request, raw)}
        review = {'scores': {key:2 for key in DIMENSIONS}, 'criticalFailures': [],
                  'verdict':'provisional-accept', 'reason':'Fixture only'}
        critique = {'caseId':'one', 'inputSha256':digest(request), 'valid':True, 'candidate':review,
                    'raw':{'text':canonical(review), 'stopReason':'stop'}}
        identity = {'engine':{'sourcePredictionsSha256':digest([prediction])}}
        cases = [({'caseId':'one'},request)]
        accepted, rejected = select_provisional(cases,[prediction],[critique],identity)
        self.assertEqual((len(accepted),len(rejected)),(1,0))
        identity['engine']['sourcePredictionsSha256'] = 'wrong'
        with self.assertRaises(AuditError):
            select_provisional(cases,[prediction],[critique],identity)
        identity['engine']['sourcePredictionsSha256'] = digest([prediction])
        review['criticalFailures'] = [{'kind':'invented-fact','quote':'Compare options',
                                      'reason':'Fixture failure','evidenceIds':['positions']}]
        review['verdict'] = 'reject'
        critique['raw']['text'] = canonical(review)
        accepted, rejected = select_provisional(cases,[prediction],[critique],identity)
        self.assertEqual((len(accepted),len(rejected)),(0,1))

    def test_batched_stop_trims_eos_padding_without_hiding_length_failures(self):
        self.assertEqual(trim_tokens([10, 11, 99, 99, 99], {99}), ([10, 11, 99], 'stop'))
        self.assertEqual(trim_tokens([10, 11, 12], {99}), ([10, 11, 12], 'length'))

    def test_support_enrichment_preserves_times_and_rejects_future_endpoint(self):
        request = project_scene(*fixture())
        transition = {'episodeId': 'hidden-death', 'heroId': 1, 'anchorSeconds': 100, 'allyHeroId': 2,
                      'expectedPairedSeconds': 11, 'observedPairedSeconds': 6, 'change': 2, 'unavailableReason': None,
                      'earlier': {'targetSeconds': 90, 'viewerSampleSeconds': 90, 'allySampleSeconds': 90, 'distance': 500.123},
                      'atAnchor': {'targetSeconds': 100, 'viewerSampleSeconds': 99, 'allySampleSeconds': 100, 'distance': 300.456}}
        enriched = add_support(copy.deepcopy(request), transition)
        fact = enriched['facts'][-1]
        self.assertEqual(fact['value']['earlier']['distance'], 500.12)
        self.assertNotIn('episodeId', fact['value'])
        self.assertEqual(fact['value']['observedPairedSeconds'], 6)
        transition['atAnchor']['allySampleSeconds'] = 101
        with self.assertRaises(AuditError):
            add_support(copy.deepcopy(request), transition)

    def test_manifest_split_and_source_hash_are_checked_before_projection(self):
        context, scene = fixture()
        context.update(matchId='1', scenes=[scene])
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root/'decisions').mkdir()
            path = root/'decisions/1-1.json'
            path.write_text(canonical(context), encoding='utf-8')
            manifest = {'matches': [{'matchId': '1', 'split': 'train', 'sourceHash': 'source-hash'}]}
            (root/'manifest.json').write_text(canonical(manifest))
            record = {'caseId': 'case', 'matchId': '1', 'heroId': 1, 'sceneId': 'death-secret',
                      'anchorSeconds': 100, 'sourceSplit': 'train', 'sourceMatchHash': 'source-hash',
                      'sourcePath': 'decisions/1-1.json', 'sourceDocumentSha256': hashlib.sha256(path.read_bytes()).hexdigest()}
            index = root/'index.jsonl'
            index.write_text(canonical(record)+'\n')
            self.assertEqual(len(load_cases(root, index)), 1)
            manifest['matches'][0]['split'] = 'reserved-test'
            (root/'manifest.json').write_text(canonical(manifest))
            with self.assertRaises(AuditError):
                load_cases(root, index)
            manifest['matches'][0]['split'] = 'train'
            (root/'manifest.json').write_text(canonical(manifest))
            path.write_text(canonical({**context, 'changed': True}))
            with self.assertRaises(AuditError):
                load_cases(root, index)

    def test_critic_cannot_approve_its_own_report_with_critical_failures_or_false_quotes(self):
        request = project_scene(*fixture())
        review = {'scores': {key: 2 for key in DIMENSIONS}, 'criticalFailures': [],
                  'verdict': 'provisional-accept', 'reason': 'Fixture only'}
        validate_critique(request, candidate(), review)
        review['criticalFailures'] = [{'kind': 'invented-fact', 'quote': 'not in candidate',
                                      'reason': 'test', 'evidenceIds': ['positions']}]
        review['verdict'] = 'reject'
        with self.assertRaises(AuditError):
            validate_critique(request, candidate(), review)
        review['criticalFailures'][0]['quote'] = 'Compare options'
        validate_critique(request, candidate(), review)
        review['verdict'] = 'provisional-accept'
        with self.assertRaises(AuditError):
            validate_critique(request, candidate(), review)

    def test_even_closed_json_at_token_limit_does_not_receive_valid_credit(self):
        request = project_scene(*fixture())
        result = score_generation(request, {'text': canonical(candidate()), 'stopReason': 'length'})
        self.assertFalse(result['valid'])
        self.assertEqual(result['error'], 'token-limit')

    def test_resume_avoids_duplicate_generation_and_rejects_changed_identity(self):
        request = project_scene(*fixture())
        cases = [({'caseId': 'one'}, request)]
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)/'run'
            calls = []
            def generate(value):
                calls.append(value)
                return {'text': canonical(candidate())}
            self.assertEqual(run_cases(cases, output, {'engine': 'fake'}, generate)['valid'], 1)
            run_cases(cases, output, {'engine': 'fake'}, generate)
            self.assertEqual(len(calls), 1)
            with self.assertRaises(AuditError):
                run_cases(cases, output, {'engine': 'changed'}, generate)
            self.assertFalse((output/'.tactical.lock').exists())
            with (output/'predictions.jsonl').open('ab') as stream:
                stream.write(b'{')
            with self.assertRaises(AuditError):
                run_cases(cases, output, {'engine': 'fake'}, generate)

    def test_projection_is_invariant_to_outcome_and_identity(self):
        context, scene = fixture()
        before = project_scene(context, scene)
        scene['retrospectiveEpisode'] = {'id': 'fight-other', 'eventSeconds': 99999}
        context['matchId'] = 'different'
        self.assertEqual(project_scene(context, scene), before)
        self.assertNotIn('retrospectiveEpisode', before)

    def test_nested_unrecognized_fields_do_not_enter_prompt(self):
        context, scene = fixture()
        before = project_scene(context, scene)
        for point in scene['atOrBeforeAnchor']['positions']:
            point['futureDeath'] = 'secret'
        scene['atOrBeforeAnchor']['economy']['eventualWinner'] = 'secret'
        scene['atOrBeforeAnchor']['recentPurchases'][0]['futureUse'] = 'secret'
        self.assertEqual(project_scene(context, scene), before)

    def test_ties_and_null_are_preserved_and_count_excludes_viewer(self):
        context, scene = fixture()
        request = project_scene(context, scene)
        facts = {f['id']: f['value'] for f in request['facts']}
        self.assertEqual(facts['positions']['observedAllies'], 1)
        self.assertEqual(len(facts['purchases']['latestEvents']), 2)
        self.assertEqual(facts['objectives']['status'], 'unavailable')
        self.assertEqual(facts['upgrades']['status'], 'none-recorded')
        scene['atOrBeforeAnchor']['positions'] = []
        scene['atOrBeforeAnchor']['missingHeroPositions'] = 10
        self.assertIsNone(project_scene(context, scene)['facts'][0]['value']['observedAllies'])

    def test_pregame_purchase_within_window_is_not_a_future_or_invalid_event(self):
        context, scene = fixture()
        facts = scene['atOrBeforeAnchor']
        facts.update(anchorSeconds=0, positions=[], missingHeroPositions=10, economy=None,
                     recentPurchases=[{'seconds': -20, 'heroId': 1, 'itemKey': 'branches'}])
        request = project_scene(context, scene)
        purchases = next(f for f in request['facts'] if f['id'] == 'purchases')
        self.assertEqual(purchases['value']['events'][0]['seconds'], -20)

    def test_future_and_stale_sources_rejected_without_mutation(self):
        for kind in ('position', 'purchase', 'economy'):
            context, scene = fixture()
            facts = scene['atOrBeforeAnchor']
            if kind == 'position':
                facts['positions'][0]['sampleSeconds'] = 101
            elif kind == 'purchase':
                facts['recentPurchases'][0]['seconds'] = 101
            else:
                facts['economy']['sampleSeconds'] = 39
            before = copy.deepcopy(scene)
            with self.assertRaises(AuditError):
                project_scene(context, scene)
            self.assertEqual(before, scene)

    def test_disposition_and_unknown_refs_enforced(self):
        context, scene = fixture()
        request = project_scene(context, scene)
        self.assertEqual(validate_candidate(request, candidate()), candidate())
        for change in ({'preference': 'A'}, {'evidenceIds': ['outcome']}, {'extra': True}):
            value = candidate()
            value.update(change)
            with self.assertRaises(AuditError):
                validate_candidate(request, value)
        value = candidate()
        value['disposition'] = 'insufficient'
        value['alternatives'] = []
        validate_candidate(request, value)
        value['preference'] = 'B'
        with self.assertRaises(AuditError):
            validate_candidate(request, value)


if __name__ == '__main__':
    unittest.main()
