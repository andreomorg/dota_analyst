"""Task identity and explicit promotion records; no installation or tactical approval inference."""
import argparse
import json
from pathlib import Path

from ..core.json import parse_json, digest
from ..core.streams import iter_json_array
from ..core.validation import require
from ..hashing import file_hash


def finalize_candidate(root):
    root = Path(root)
    capability = parse_json((root / 'model-capability.json').read_text(encoding='utf-8'))
    frozen = parse_json((root / 'freeze.json').read_text(encoding='utf-8'))
    require(frozen.get('capabilityHash') == file_hash(root / 'model-capability.json'), 'Capability freeze mismatch')
    files = {}
    for name in ('adapter_config.json', 'adapter_model.safetensors'):
        path = root / 'output/adapter-final' / name
        require(path.is_file() and not path.is_symlink() and path.stat().st_size > 0, 'Missing candidate artifact')
        files[name] = file_hash(path)
    candidate = {**capability, 'adapterFiles': files, 'freezeSha256': file_hash(root / 'freeze.json')}
    path = root / 'model-candidate.json'
    if path.exists():
        require(parse_json(path.read_text(encoding='utf-8')) == candidate, 'Candidate identity changed')
    else:
        with path.open('x', encoding='utf-8') as stream:
            json.dump(candidate, stream, ensure_ascii=False, indent=2)
    return candidate


def experiment_capability(root, cfg):
    root = Path(root)
    profile = cfg.get('profile', 'factual-review')
    policies = set()
    for stem in ('development', 'validation'):
        for row in iter_json_array(root / (stem + '-metadata.json')):
            policies.add(row['request'].get('reviewPolicyVersion', 1))
    require(policies and all(type(value) is int and value in (1, 2) for value in policies), 'Unknown review policy')
    return {'schemaVersion': 1, 'task': profile, 'inputSchema': profile + ':request-v1',
            'outputSchema': profile + ':response-v1', 'policyVersions': sorted(policies),
            'baseModel': cfg['model'], 'baseRevision': cfg['revision'], 'tokenizerRevision': cfg['revision'],
            'chatTemplate': 'no-thinking-assistant-eos-v1', 'evaluator': 'coach-ml-paired-evaluation-v2',
            'datasetHashes': {f'{stem}.jsonl': file_hash(root / (stem + '.jsonl'))
                              for stem in ('development', 'validation')}}


def require_compatible(capability, task, input_schema, output_schema):
    require(isinstance(capability, dict) and type(capability.get('schemaVersion')) is int
            and capability['schemaVersion'] == 1, 'Invalid model capability manifest')
    require((capability.get('task'), capability.get('inputSchema'), capability.get('outputSchema'))
            == (task, input_schema, output_schema), 'Model capability is incompatible with serving contract')


def record_promotion(capability_path, evaluation_path, decision_path, output):
    capability = parse_json(Path(capability_path).read_text(encoding='utf-8'))
    decision = parse_json(Path(decision_path).read_text(encoding='utf-8'))
    evaluation = parse_json(Path(evaluation_path).read_text(encoding='utf-8'))
    require(isinstance(decision, dict), 'Expected explicit promotion decision')
    require_compatible(capability, decision.get('task'), decision.get('inputSchema'), decision.get('outputSchema'))
    require(isinstance(capability.get('adapterFiles'), dict) and capability['adapterFiles']
            and isinstance(capability.get('freezeSha256'), str), 'Promotion requires a trained artifact identity')
    require(decision.get('capabilitySha256') == file_hash(capability_path)
            and decision.get('evaluationSha256') == file_hash(evaluation_path), 'Decision artifact identity mismatch')
    require(decision.get('approved') is True and isinstance(decision.get('reviewer'), str)
            and bool(decision['reviewer'].strip()) and isinstance(decision.get('reason'), str)
            and bool(decision['reason'].strip()), 'Explicit reviewed promotion decision required')
    require(isinstance(evaluation, dict) and evaluation.get('structuralIntegrityPassed') is True
            and evaluation.get('tacticalQualityApproved') is True, 'Evaluation has no tactical approval')
    require(isinstance(decision.get('rollbackModel'), str) and bool(decision['rollbackModel']), 'Rollback model required')
    record = {'schemaVersion': 1, 'kind': 'promotion-decision', 'capability': capability,
              'decision': decision, 'evaluationSha256': file_hash(evaluation_path),
              'capabilityIdentity': digest(capability), 'installed': False}
    with Path(output).open('x', encoding='utf-8') as stream:
        json.dump(record, stream, ensure_ascii=False, indent=2)
    return record


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml promotion-check')
    for name in ('capability', 'evaluation', 'decision', 'output'):
        parser.add_argument('--' + name, required=True, type=Path)
    args = parser.parse_args(argv)
    record_promotion(args.capability, args.evaluation, args.decision, args.output)
    print(json.dumps({'decisionRecorded': True, 'installed': False}))
    return 0
