"""Bind archived development cases to metadata without certifying game mechanics."""
import copy
import hashlib
import re
from datetime import datetime

from ..core.json import parse_json
from ..core.validation import require, AuditError


def _sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _load(directory, source, index):
    audit = parse_json((directory/'export-audit.json').read_text(encoding='utf-8-sig'))
    require(type(audit.get('schemaVersion')) is int and audit['schemaVersion'] == 1
            and audit.get('developmentOnly') is True and audit.get('mechanicsVerified') is False,
            'Unsupported patch receipt')
    require(audit['sourceManifestSha256'] == _sha(source/'manifest.json'), 'Patch manifest changed')
    require(any(row['name'] == index.name and row['sha256'] == _sha(index) for row in audit['sourceIndexes']),
            'Patch receipt does not cover selected index')
    path = directory/'patches.jsonl'
    require(audit['dataFileSha256'] == _sha(path), 'Patch data hash changed')
    manifest = parse_json((source/'manifest.json').read_text(encoding='utf-8-sig'))
    declared = {str(row['matchId']):row for row in manifest['matches']}
    rows = {}
    for line in path.read_text(encoding='utf-8').splitlines():
        row = parse_json(line)
        require(type(row) is dict, 'Malformed patch row')
        origin = row.get('source')
        common = {'matchId','sourceMatchHash','patchId','source','mechanicsVerified'}
        extra = {'metadataPresent'} if origin == 'stored-match:patch-id' else {'url','retrievedAt','responseSha256','status'}
        require(origin in ('stored-match:patch-id','opendota:match.patch') and set(row) == common|extra,
                'Unexpected patch metadata fields')
        identifier, patch = row['matchId'], row['patchId']
        require(type(identifier) is str and re.fullmatch(r'[0-9]+',identifier) and identifier not in rows,
                'Invalid or repeated patch match')
        original = declared.get(identifier)
        require(original is not None and original['split'] in ('train','train-known')
                and row['sourceMatchHash'] == original['sourceHash'], 'Patch source identity mismatch')
        require(patch is None or type(patch) is int and 0 <= patch <= 2147483647, 'Invalid patch ID')
        require(row['mechanicsVerified'] is False, 'Metadata must not certify mechanics')
        if origin == 'stored-match:patch-id':
            require(type(row['metadataPresent']) is bool and (row['metadataPresent'] or patch is None),
                    'Stored patch availability contradiction')
        else:
            require(patch is not None and type(row['status']) is int and row['status'] == 200
                    and row['url'] == 'https://api.opendota.com/api/matches/'+identifier,
                    'Invalid public patch origin')
            require(type(row['responseSha256']) is str and re.fullmatch(r'[0-9a-f]{64}',row['responseSha256']),
                    'Missing metadata response fingerprint')
            require(type(row['retrievedAt']) is str and row['retrievedAt'].endswith('Z'), 'Missing UTC retrieval time')
            stamp = datetime.fromisoformat(row['retrievedAt'].replace('Z','+00:00'))
            require(stamp.tzinfo is not None, 'Invalid metadata retrieval time')
        rows[identifier] = row
    require(type(audit.get('rows')) is int and audit['rows'] > 0 and len(rows) == audit['rows'],
            'Patch receipt count mismatch')
    return rows, _sha(path)


def attach_patches(cases, directory, source, index):
    try:
        rows, fingerprint = _load(directory,source,index)
        output = []
        for record, request in cases:
            row = rows.get(record['matchId'])
            require(row is not None and row['sourceMatchHash'] == record['sourceMatchHash'], 'Patch case mismatch')
            q = copy.deepcopy(request)
            old, patch = q['patch'], row['patchId']
            require(old['mechanicsVerified'] is False, 'Cannot replace a verified mechanics binding with metadata')
            if patch is not None:
                require(old['matchPatch'] is None or old['matchPatch'] == str(patch), 'Conflicting existing patch')
                q['patch'] = {**old,'matchPatch':str(patch),'source':row['source'],'mechanicsVerified':False}
            # A known patch ID is not verified mechanics. Existing unknowns stay unchanged.
            output.append(({**record,'patchSourceSha256':fingerprint},q))
        return output
    except AuditError:
        raise
    except (KeyError,TypeError,AttributeError,ValueError) as error:
        raise AuditError('Malformed patch receipt') from error
