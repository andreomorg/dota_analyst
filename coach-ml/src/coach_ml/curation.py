"""Remove repeated projected inputs into a new, audited dataset directory."""
import argparse
import json
from pathlib import Path
import shutil
import sys

from .core.validation import AuditError, require
from .core.json import canonical, digest
from .core.streams import iter_json_array, iter_jsonl
from .hashing import file_hash
from .datasets import audit_experiment


def deduplicate(source, output, manifest, profile='factual-review'):
    source, output, manifest = Path(source).resolve(), Path(output).resolve(), Path(manifest).resolve()
    require(source != output and not output.exists(), 'Output must be a new directory')
    names = ['development.jsonl', 'development-metadata.json', 'validation.jsonl',
             'validation-metadata.json', 'instructions.txt']
    initial_hashes = {name: file_hash(source / name) for name in names}
    manifest_hash = file_hash(manifest)
    original = audit_experiment(source, manifest, profile)
    output.mkdir(parents=True, exist_ok=False)
    seen, removed = set(), {}
    for partition, stem in [('train', 'development'), ('validation', 'validation')]:
        old_games, kept_games, row_count, duplicates = set(), set(), 0, 0
        with (output / (stem + '.jsonl.tmp')).open('w', encoding='utf-8') as data, \
                (output / (stem + '-metadata.json.tmp')).open('w', encoding='utf-8') as metadata:
            metadata.write('[')
            rows = iter_jsonl(source / (stem + '.jsonl'))
            metas = iter_json_array(source / (stem + '-metadata.json'))
            for record, meta in zip(rows, metas, strict=True):
                old_games.add(meta['matchId'])
                signature = digest(meta['request'])
                if signature in seen:
                    duplicates += 1
                    continue
                seen.add(signature)
                kept_games.add(meta['matchId'])
                meta['row'] = row_count
                data.write(canonical(record) + '\n')
                metadata.write((',' if row_count else '') + canonical(meta))
                row_count += 1
            metadata.write(']\n')
        require(old_games == kept_games, f'Deduplication would remove every example of a {partition} match')
        removed[partition] = duplicates
    require(initial_hashes == {name: file_hash(source / name) for name in names}
            and manifest_hash == file_hash(manifest), 'Source changed during curation')
    for stem in ['development', 'validation']:
        for suffix in ['.jsonl', '-metadata.json']:
            (output / (stem + suffix + '.tmp')).rename(output / (stem + suffix))
    shutil.copyfile(source / 'instructions.txt', output / 'instructions.txt')
    final = audit_experiment(output, manifest, profile)
    require(final['inputIsolationPassed'], 'Final input isolation failed')
    report = {'schemaVersion': 1, 'kind': 'deduplicated-dataset', 'profile': profile,
              'sourceFiles': initial_hashes, 'manifestSha256': manifest_hash,
              'removedDuplicateInputs': removed, 'matchCoveragePreserved': True,
              'exactSubset': True, 'inputIsolationPassed': True,
              'examplesBefore': {p: a['rows'] for p, a in original['data'].items()},
              'examplesAfter': {p: a['rows'] for p, a in final['data'].items()},
              'data': final['data'], 'trainingExecuted': False, 'humanApproved': False,
              'outputFiles': {name: file_hash(output / name) for name in names}}
    (output / 'curation-audit.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml deduplicate', description=__doc__)
    for flag in ['source', 'output', 'manifest']:
        parser.add_argument('--' + flag, required=True, type=Path)
    parser.add_argument('--profile', choices=['factual-review', 'competency-review-v1'], default='factual-review')
    args = parser.parse_args(argv)
    try:
        result = deduplicate(args.source, args.output, args.manifest, args.profile)
        print(json.dumps({k: result[k] for k in ['kind', 'examplesAfter', 'removedDuplicateInputs', 'inputIsolationPassed']}))
        return 0
    except (AuditError, OSError, ValueError, KeyError, TypeError) as exc:
        print(json.dumps({'error': str(exc), 'trainingExecuted': False}), file=sys.stderr)
        return 2
