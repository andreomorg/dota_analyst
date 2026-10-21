"""Recompute published item-property history; never certify an actual replay build."""
import argparse
import hashlib
import re
from datetime import datetime
from pathlib import Path

from ..core.json import canonical, parse_json
from ..core.validation import require, AuditError

REPOSITORY = 'odota/dotaconstants'
SOURCE_FILE = 'build/items.json'
ITEMS = ('magic_stick','tpscroll','blink')
BEHAVIORS = {'Instant Cast','No Target','Point Target','Channeled'}


def _timestamp(value):
    require(type(value) is str and value.endswith('Z'), 'Source timestamp must be UTC')
    return datetime.fromisoformat(value.replace('Z','+00:00'))


def _read(path):
    raw = path.read_bytes()
    require(len(raw) <= 4*1024*1024, 'Oversized source artifact')
    return parse_json(raw.decode('utf-8-sig')), hashlib.sha256(raw).hexdigest()


def _commit(row):
    require(type(row) is dict and set(row) == {'sha','committedAt','url'}, 'Unexpected commit fields')
    require(type(row['sha']) is str and re.fullmatch('[0-9a-f]{40}',row['sha']), 'Invalid commit identity')
    require(row['url'] == 'https://github.com/'+REPOSITORY+'/commit/'+row['sha'], 'Invalid commit origin')
    return _timestamp(row['committedAt'])


def _fields(document):
    result = {}
    for key in ITEMS:
        item = document[key]
        behavior = item['behavior']
        behavior = [behavior] if type(behavior) is str else behavior
        require(type(behavior) is list and 1 <= len(behavior) <= 4
                and all(type(b) is str and b in BEHAVIORS for b in behavior)
                and len(set(behavior)) == len(behavior), 'Unreviewed or invalid source behavior')
        team, target = item.get('target_team'), item.get('target_type')
        require(team in (None,'Friendly','Enemy','Both') and target in (None,'Building','Hero','Basic'),
                'Unreviewed target metadata')
        result['item_'+key] = {'behavior':sorted(behavior),'targetTeam':team,'targetType':target}
    return result


def audit_reference(directory, patch_map, patch_id):
    try:
        return _audit_reference(directory.resolve(),patch_map,patch_id)
    except AuditError:
        raise
    except (KeyError,TypeError,ValueError,AttributeError,IndexError) as error:
        raise AuditError('Malformed published source history') from error


def _audit_reference(directory, patch_map, patch_id):
    history, history_hash = _read(directory/'history.json')
    audit, audit_hash = _read(directory/'property-audit.json')
    require(type(history.get('schemaVersion')) is int and history['schemaVersion'] == 1
            and type(audit.get('schemaVersion')) is int and audit['schemaVersion'] == 1,
            'Unsupported source history schema')
    require(history['repository'] == REPOSITORY and history['file'] == SOURCE_FILE,
            'Unreviewed source repository or file')
    require(history['historyPaginationComplete'] is True and audit['historyPaginationComplete'] is True
            and audit['allListedSnapshotsPresent'] is True, 'Published source history is incomplete')
    require(audit['repository'] == REPOSITORY and audit['scope'] == 'published-source-history-only'
            and audit['since'] == history['since'] and audit['until'] == history['until'], 'Source scope mismatch')
    start, end = _timestamp(history['since']), _timestamp(history['until'])
    require(start < end, 'Invalid source interval')
    mapping, mapping_hash = _read(patch_map)
    require(type(patch_id) is int and patch_id >= 0 and type(mapping) is list, 'Invalid patch mapping')
    ids = [p['id'] for p in mapping]
    require(all(type(i) is int and i >= 0 for i in ids) and len(set(ids)) == len(ids), 'Invalid patch identities')
    matches = [p for p in mapping if p['id'] == patch_id]
    require(len(matches) == 1, 'Missing published patch ID')
    patch = matches[0]
    require(type(patch['name']) is str and re.fullmatch(r'[0-9]+\.[0-9]+[a-z]?',patch['name']),
            'Invalid published patch name')
    patch_start = _timestamp(patch['date'])
    require(start <= patch_start <= end, 'History does not include patch start')
    require(not any(p['id'] != patch_id and patch_start < _timestamp(p['date']) <= end for p in mapping),
            'Source interval crosses another published patch')
    baseline, commits = history['baseline'], history['commits']
    require(type(commits) is list and 1 <= len(commits) <= 100, 'No reviewed source updates')
    require(_commit(baseline) <= start, 'Missing baseline at source interval start')
    times = [_commit(c) for c in commits]
    require(all(start < t <= end for t in times) and times == sorted(times,reverse=True),
            'Invalid source revision chronology')
    expected = [baseline,*reversed(commits)]
    require(len({c['sha'] for c in expected}) == len(expected), 'Repeated source commit')
    snapshots = audit['snapshots']
    require(type(snapshots) is list and len(snapshots) == len(expected), 'Missing or extra source snapshot')
    actual, identities = [], []
    for reference, snapshot in zip(expected,snapshots):
        require(all(snapshot[k] == reference[k] for k in ('sha','committedAt')), 'Snapshot commit mismatch')
        relative = 'items-'+reference['sha']+'.json'
        require(snapshot['file'] == relative and snapshot['url'] ==
                'https://raw.githubusercontent.com/'+REPOSITORY+'/'+reference['sha']+'/'+SOURCE_FILE,
                'Invalid source snapshot origin')
        path = (directory/relative).resolve()
        require(path.is_relative_to(directory), 'Snapshot escaped source directory')
        document, fingerprint = _read(path)
        require(fingerprint == snapshot['sha256'], 'Source snapshot hash changed')
        fields = _fields(document)
        require(canonical(fields) == canonical(snapshot['fields']), 'Declared item fields differ from source')
        actual.append(fields)
        identities.append({'commit':reference['sha'],'sha256':fingerprint,'url':snapshot['url']})
    stable, entries = {}, []
    for key in ('item_'+k for k in ITEMS):
        stable[key] = {field:all(s[key][field] == actual[0][key][field] for s in actual)
                       for field in ('behavior','targetTeam','targetType')}
        values = {field:actual[0][key][field] for field,unchanged in stable[key].items() if unchanged}
        entries.append({'technicalKey':key,'stablePublishedProperties':values,
                        'unstableProperties':[field for field,unchanged in stable[key].items() if not unchanged]})
    require(canonical(stable) == canonical(audit['stableProperties']), 'False source stability declaration')
    return {'schemaVersion':1,'kind':'published-item-property-reference','patchId':patch_id,
            'publishedPatchName':patch['name'],'interval':{'since':history['since'],'until':history['until']},
            'repository':REPOSITORY,'sourceFile':SOURCE_FILE,'historySha256':history_hash,
            'propertyAuditSha256':audit_hash,'patchMapSha256':mapping_hash,'sourceSnapshots':identities,'entries':entries,
            'listedSourceSnapshotsVerified':True,'gameUpdateCaptureCompletenessVerified':False,
            'mechanicsVerifiedForReplay':False,'allowedForTraining':False,'trainingStarted':False,
            'limitations':['Published history is not proof that every game update was captured.',
                          'Missing target metadata means unpublished, not an inferred targeting mode.',
                          'Activation metadata is not target, effect, success or readiness in a replay.']}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--history',type=Path,required=True)
    parser.add_argument('--patch-map',type=Path,required=True)
    parser.add_argument('--patch-id',type=int,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args = parser.parse_args(argv)
    require(not args.output.exists(), 'Reference packet output must be new')
    report = audit_reference(args.history,args.patch_map,args.patch_id)
    with args.output.open('x',encoding='utf-8') as stream:
        stream.write(canonical(report)+'\n')
    print(canonical({'entries':len(report['entries']),'snapshots':len(report['sourceSnapshots']),
                     'mechanicsVerifiedForReplay':False,'allowedForTraining':False}))


if __name__ == '__main__':
    main()
