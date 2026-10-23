"""Post-training contract/lexical comparison. Reference text equality is not tactical accuracy."""
from itertools import zip_longest
from collections import Counter
from pathlib import Path
import os

from ..core.json import parse_json, canonical
from ..core.streams import iter_jsonl, iter_json_array
from ..core.validation import require, AuditError
from ..hashing import file_hash
from .audit import validate_request
from .contracts import validate_candidate
from .facts import audit_candidate_facts


def score(request, text, truncated):
    if truncated:
        return {'valid':False,'lexicalFlags':[],'truncated':True,'error':'truncated'}
    try:
        value = parse_json(text)
        validate_candidate(request,value)
        flags = audit_candidate_facts(request,value)['flags']
        return {'valid':True,'lexicalFlags':flags,'truncated':False,'error':None}
    except (AuditError,KeyError,TypeError) as error:
        return {'valid':False,'lexicalFlags':[],'truncated':False,'error':str(error)}


def evaluate_tactical(root):
    root = Path(root)
    report, cases_file = root/'evaluation-audit.json', root/'evaluation-cases.jsonl'
    require(not report.exists() and not cases_file.exists(), 'Tactical evaluation output already exists')
    names = ['validation-metadata.json','output/predictions-base.jsonl','output/predictions-adapted.jsonl']
    fingerprints = {name:file_hash(root/name) for name in names}
    metadata = list(iter_json_array(root/'validation-metadata.json'))
    require(bool(metadata), 'Empty tactical validation')
    profile = parse_json((root/'config.json').read_text(encoding='utf-8-sig')).get('profile') if (root/'config.json').exists() else 'tactical-review-v1'
    require(profile in ('tactical-review-v1', 'tactical-review-v5'), 'Invalid tactical profile')
    identities = set()
    for index, meta in enumerate(metadata):
        require(type(meta.get('row')) is int and meta['row'] == index, 'Tactical evaluation metadata order mismatch')
        require(type(meta.get('matchId')) is str and bool(meta['matchId'])
                and type(meta.get('heroId')) is int and meta['heroId'] > 0
                and type(meta.get('episodeId')) is str and bool(meta['episodeId']), 'Invalid tactical source identity')
        identity = f"{meta['matchId']}/{meta['heroId']}/{meta['episodeId']}/{profile}"
        require(meta.get('exampleId') == identity and identity not in identities, 'Duplicate or invalid tactical example')
        identities.add(identity)
        validate_request(meta['request'], profile)
        require(meta['request']['heroId'] == meta['heroId'], 'Tactical evaluation hero mismatch')
        validate_candidate(meta['request'],meta['target'])
    metrics, results = {}, {}
    sentinel = object()
    for variant in ('base','adapted'):
        counts = Counter()
        rows = []
        for index, (meta, prediction) in enumerate(zip_longest(metadata,iter_jsonl(root/'output'/f'predictions-{variant}.jsonl'),fillvalue=sentinel)):
            require(meta is not sentinel and prediction is not sentinel, 'Incomplete tactical predictions')
            require(type(prediction.get('row')) is int and prediction['row'] == index and prediction.get('variant') == variant,
                    'Tactical prediction order mismatch')
            for key in ('matchId','heroId','episodeId','exampleId'):
                require(type(prediction.get(key)) is type(meta.get(key)) and prediction.get(key) == meta.get(key),
                        'Tactical prediction identity mismatch: '+key)
            require(type(prediction.get('hitTokenLimit')) is bool and type(prediction.get('text')) is str, 'Invalid tactical prediction fields')
            require(type(prediction.get('newTokens')) is int and prediction['newTokens'] > 0, 'Invalid tactical token count')
            value = score(meta['request'],prediction['text'],prediction['hitTokenLimit'])
            rows.append(value)
            counts['cases'] += 1
            counts['strictValid'] += value['valid']
            counts['casesWithLexicalFlags'] += bool(value['lexicalFlags'])
            counts['truncated'] += value['truncated']
        metrics[variant] = {'variant':variant,**counts}
        results[variant] = rows
    with cases_file.open('x',encoding='utf-8') as stream:
        for index, meta in enumerate(metadata):
            stream.write(canonical({'row':index,'matchId':meta['matchId'],'exampleId':meta['exampleId'],
                                     'base':results['base'][index],'adapted':results['adapted'][index]})+'\n')
    require(fingerprints == {name:file_hash(root/name) for name in names}, 'Tactical evaluation source changed')
    paired = {key:sum(before['valid']==a and after['valid']==b for before,after in zip(results['base'],results['adapted']))
              for key,a,b in [('bothValid',True,True),('gainedContract',False,True),('lostContract',True,False),('bothInvalid',False,False)]}
    result = {'schemaVersion':1,'kind':'tactical-contract-comparison','profile':profile,
              'summary':list(metrics.values()),'pairedContract':paired,'games':len({m['matchId'] for m in metadata}),
              'sourceFiles':fingerprints,'caseReport':{'file':cases_file.name,'sha256':file_hash(cases_file)},
              'structuralIntegrityPassed':True,'tacticalQualityApproved':False,'humanReviewed':False,
              'note':'Validity/lexical checks do not measure tactical usefulness or validate the reference advice. Content review is pending.'}
    temporary = root/'evaluation-audit.json.tmp'
    with temporary.open('x',encoding='utf-8') as stream:
        stream.write(canonical(result)+'\n')
    os.replace(temporary,report)
    return result
