"""Export only aligned provisional candidates; never certify tactical truth or start training."""
import argparse
from pathlib import Path

from ..core.json import canonical, digest, parse_json
from ..core.validation import require
from .pipeline import load_selection, score_generation, SYSTEM
from .curation import validate_critique
from .prompt_v5 import SYSTEM_V5
from .audit import validate_request
from .facts import audit_candidate_facts


def select_provisional(cases, predictions, critiques, critique_identity):
    require(len(cases) == len(predictions) == len(critiques), 'Curator requires complete aligned sets')
    require(critique_identity['engine'].get('sourcePredictionsSha256') == digest(predictions),
            'Critic evaluated a different prediction artifact')
    accepted, rejected = [], []
    for (record, request), prediction, critique in zip(cases, predictions, critiques):
        fingerprint = digest(request)
        require(all(row['caseId'] == record['caseId'] and row['inputSha256'] == fingerprint
                    for row in (prediction, critique)), 'Curator case mismatch')
        rescored = score_generation(request, prediction['raw'])
        require(all(prediction[key] == rescored[key] for key in ('valid', 'candidate', 'error')), 'Tampered generator verdict')
        reason = None
        if not prediction['valid']:
            reason = 'generator-contract-or-truncation'
        elif audit_candidate_facts(request, prediction['candidate'])['flags']:
            reason = 'requires-factual-content-review'
        elif not critique['valid']:
            reason = 'critic-contract'
        else:
            review = parse_json(critique['raw']['text'])
            validate_critique(request, prediction['candidate'], review)
            require(review == critique['candidate'], 'Tampered critic verdict')
            if critique['raw'].get('stopReason') == 'length':
                reason = 'critic-truncated'
            elif review['verdict'] != 'provisional-accept':
                reason = review['verdict']
        if reason:
            rejected.append({'caseId': record['caseId'], 'reason': reason})
        else:
            accepted.append((record, request, prediction['candidate']))
    return accepted, rejected


def apply_content_review(cases, predictions, accepted, rejected, review):
    require(type(review) is dict and set(review)=={'schemaVersion','reviewer','humanReviewed','sourcePredictionsSha256','decisions'},
            'Invalid content review receipt')
    require(type(review['schemaVersion']) is int and review['schemaVersion']==1
            and review['reviewer']=='assistant-provisional' and review['humanReviewed'] is False,
            'Content review must identify provisional assistant origin')
    require(review['sourcePredictionsSha256']==digest(predictions), 'Content review refers to different predictions')
    decisions=review['decisions']
    require(type(decisions) is list and len(decisions)==len(cases), 'Content review must cover every case')
    expected={record['caseId']:digest(request) for record,request in cases}
    require(len(expected)==len(cases), 'Duplicate source cases')
    seen=set()
    vetoes=set()
    for decision in decisions:
        require(type(decision) is dict and set(decision)=={'caseId','inputSha256','decision','reason'}, 'Invalid content decision')
        identifier=decision['caseId']
        require(type(identifier) is str and identifier in expected and identifier not in seen, 'Unknown or duplicate reviewed case')
        require(decision['inputSha256']==expected[identifier], 'Content review input mismatch')
        require(decision['decision'] in ('keep-provisional','veto')
                and type(decision['reason']) is str and 1 <= len(decision['reason']) <= 2000, 'Invalid content review decision')
        seen.add(identifier)
        if decision['decision']=='veto':
            vetoes.add(identifier)
    removed=[{'caseId':record['caseId'],'reason':'assistant-content-veto'}
             for record,_,_ in accepted if record['caseId'] in vetoes]
    return [row for row in accepted if row[0]['caseId'] not in vetoes], [*rejected,*removed]


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ('source', 'generation', 'review', 'output'):
        parser.add_argument('--'+key, type=Path, required=True)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument('--index',type=Path)
    selection.add_argument('--bundle',type=Path)
    parser.add_argument('--minimum-accepted', type=int, required=True)
    parser.add_argument('--include-support', action='store_true')
    parser.add_argument('--content-review',type=Path)
    parser.add_argument('--partition', choices=['development','validation-diagnostic'], default='development')
    parser.add_argument('--actions',type=Path,help='Opt-in C# action receipt directory (development only)')
    parser.add_argument('--patches',type=Path,help='Opt-in audited development patch metadata directory')
    parser.add_argument('--profile', choices=['tactical-review-v1', 'tactical-review-v5'], default='tactical-review-v1')
    parser.add_argument('--situated-requests', type=Path,
                        help='Aligned canonical v5 requests for the selected source scenes')
    args = parser.parse_args(argv)
    system = SYSTEM_V5 if args.profile == 'tactical-review-v5' else SYSTEM
    require(args.minimum_accepted > 0, 'Minimum acceptance must be explicit and positive')
    require(not args.output.exists(), 'Curated output must be new')
    cases = load_selection(args.source,args.index,args.bundle,args.include_support,args.partition,args.actions,args.patches)
    if args.profile == 'tactical-review-v5':
        require(args.situated_requests is not None, 'V5 requires aligned situated requests')
        situated = parse_json(args.situated_requests.read_text(encoding='utf-8-sig'))
        require(type(situated) is list and len(situated) == len(cases), 'Incomplete situated requests')
        aligned = []
        for (record, legacy), request in zip(cases, situated):
            validate_request(request, args.profile)
            shared = {**request, 'schemaVersion': 1, 'task': 'tactical-review-v1',
                      'unknowns': legacy['unknowns'],
                      'facts': [f for f in request['facts'] if f['id'] not in ('situation', 'roster')]}
            require(canonical(shared) == canonical(legacy), 'Situated source scene mismatch')
            aligned.append((record, request))
        cases = aligned
    else:
        require(args.situated_requests is None, 'Situated requests require the v5 profile')
    for _, request in cases:
        validate_request(request, args.profile)
    predictions = [parse_json(line) for line in (args.generation/'predictions.jsonl').read_text().splitlines()]
    generation_identity = parse_json((args.generation/'identity.json').read_text())
    student_system = generation_identity['engine'].get('studentSystemSha256',generation_identity['systemSha256'])
    require(student_system == digest(system), 'Dataset instructions differ from generation instructions')
    critiques = [parse_json(line) for line in (args.review/'predictions.jsonl').read_text().splitlines()]
    identity = parse_json((args.review/'identity.json').read_text())
    accepted, rejected = select_provisional(cases, predictions, critiques, identity)
    content_review = parse_json(args.content_review.read_text()) if args.content_review else None
    if content_review is not None:
        accepted,rejected=apply_content_review(cases,predictions,accepted,rejected,content_review)
    args.output.mkdir(parents=True)
    metadata = []
    with (args.output/'provisional-sft.jsonl').open('x', encoding='utf-8') as stream:
        for index, (record, request, candidate) in enumerate(accepted):
            stream.write(canonical({'messages': [{'role':'system','content':system},
                                                 {'role':'user','content':canonical(request)},
                                                 {'role':'assistant','content':canonical(candidate)}]})+'\n')
            metadata.append({'row':index, 'provenance':record, 'request':request, 'target':candidate,
                             'matchId':record['matchId'], 'heroId':record['heroId'], 'episodeId':record['sceneId'],
                             'sourceHash':record['sourceMatchHash'],
                             'sourceSceneId':f"{record['matchId']}/{record['heroId']}/{record['sceneId']}",
                             'exampleId':f"{record['matchId']}/{record['heroId']}/{record['sceneId']}/{args.profile}",
                             'provisionalAccepted':True, 'sourcePredictionsSha256':digest(predictions),
                             'criticIdentitySha256':digest(identity),
                             'inputSha256':digest(request), 'labelOrigin':'model-generated-and-model-critiqued',
                             'humanReviewed':False, 'tacticalQualityApproved':False,
                             'assistantContentReviewed':content_review is not None,
                             'contentReviewSha256':digest(content_review) if content_review is not None else None})
    (args.output/'metadata.json').write_text(canonical(metadata)+'\n', encoding='utf-8')
    (args.output/'rejections.json').write_text(canonical(rejected)+'\n', encoding='utf-8')
    report = {'profile':args.profile, 'cases':len(cases), 'provisionalAccepted':len(accepted),
              'rejected':len(rejected), 'minimumAccepted':args.minimum_accepted,
              'experimentalDataReady':len(accepted) >= args.minimum_accepted,
              'trainingProfile':args.profile, 'standardExperimentAssemblyRequired':True,
              'trainingStarted':False, 'trainingApproved':False,
              'tacticalQualityApproved':False, 'humanReviewed':False,
              'criticIdentitySha256':digest(identity), 'sourcePredictionsSha256':digest(predictions),
              'teacherIdentitySha256':digest(generation_identity),'studentSystemSha256':digest(system),
              'contentReviewSha256':digest(content_review) if content_review is not None else None,
              'note':'Critic acceptance is provisional; same-model errors may be correlated. No automatic training or promotion.'}
    (args.output/'curation-audit.json').write_text(canonical(report)+'\n', encoding='utf-8')
    print(canonical(report))


if __name__ == '__main__':
    main()
