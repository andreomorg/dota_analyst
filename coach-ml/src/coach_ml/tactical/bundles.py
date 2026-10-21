"""Read audited C# control windows without inventing retrospective episodes."""
import hashlib
from pathlib import Path

from ..core.json import canonical, digest, parse_json
from ..core.validation import require
from ..core.streams import iter_jsonl
from .projection import project_scene
from .audit import validate_request


def load_bundle(source, bundle, partition='development'):
    source, bundle = Path(source).resolve(), Path(bundle).resolve()
    allowed = {'development':{'train','train-known'},'validation-diagnostic':{'validation-new','validation-known'}}
    require(partition in allowed, 'Reserved partitions are not available to control inference')
    matches = {m['matchId']:m for m in parse_json((source/'manifest.json').read_text())['matches']}
    receipt = parse_json((bundle.parent/'control-audit.json').read_text())
    independent = parse_json((bundle.parent/'independent-audit.json').read_text())
    controls_file = bundle.parent/'controls.jsonl'
    control_hash = hashlib.sha256(controls_file.read_bytes()).hexdigest()
    require(control_hash == receipt['controlFileSha256'] and independent['status'] == 'passed'
            and independent['rows'] == receipt['rows'], 'Control bundle has no matching integrity audit')
    require(hashlib.sha256((source/'manifest.json').read_bytes()).hexdigest() == receipt['sourceManifestSha256'],
            'Control source manifest changed')
    controls = {}
    for row in iter_jsonl(controls_file):
        key = (row['matchId'],row['controlId'])
        require(key not in controls, 'Repeated control identity')
        controls[key] = row
    require(len(controls) == receipt['rows'], 'Control export count mismatch')
    cases, checked_sources = [], set()
    for item in iter_jsonl(bundle):
        row = controls.get((item['matchId'],item['controlId']))
        require(row is not None and row['kind'] == 'control-window', 'Unknown control source')
        match = matches.get(row['matchId'])
        require(match is not None and match['split'] in allowed[partition]
                and match['split'] == row['sourceSplit'] and match['sourceHash'] == row['sourceHash'], 'Control partition/source mismatch')
        require(type(row['sourceHash']) is str and len(row['sourceHash']) == 64
                and all(c in '0123456789abcdef' for c in row['sourceHash']), 'Invalid control source hash')
        if row['sourceHash'] not in checked_sources:
            raw = source/'archive'/(row['sourceHash']+'.json')
            require(hashlib.sha256(raw.read_bytes()).hexdigest() == row['sourceHash'], 'Control replay source changed')
            checked_sources.add(row['sourceHash'])
        request = project_scene(row,row)
        validate_request(request)
        require(canonical(request) == canonical(item['request']) and digest(request) == item['inputSha256'], 'Control input was modified')
        identity = f"{row['matchId']}/{row['heroId']}/{row['controlId']}"
        record = {'caseId':hashlib.sha256(identity.encode()).hexdigest()[:24], 'matchId':row['matchId'],
                  'heroId':row['heroId'],'sceneId':row['controlId'],'sourceSplit':row['sourceSplit'],
                  'sourceMatchHash':row['sourceHash'],'sourceDocumentSha256':control_hash,
                  'sourceKind':'control-window','anchorSeconds':request['anchorSeconds']}
        cases.append((record,request))
    require(bool(cases) and len({r['caseId'] for r,_ in cases}) == len(cases)
            and len({digest(q) for _,q in cases}) == len(cases), 'Empty or duplicate control cases')
    return cases
