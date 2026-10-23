"""Validate experiment contracts and input isolation without loading model weights."""
import math
from pathlib import Path
import re
from ..core.json import parse_json
from ..core.validation import require
from ..datasets import audit_experiment

def load_config(experiment):
    cfg = parse_json((Path(experiment) / 'config.json').read_text(encoding='utf-8-sig'))
    require(isinstance(cfg, dict), 'Expected a training configuration object')
    require(isinstance(cfg.get('model'), str) and re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', cfg['model']), 'Expected an organization/model ID')
    require(isinstance(cfg.get('revision'), str) and re.fullmatch(r'[0-9a-f]{40}', cfg['revision']), 'Expected a pinned model commit')
    require(type(cfg.get('epochs')) is int and 1 <= cfg['epochs'] <= 4, 'Expected one to four explicit epochs')
    for name, maximum in [('samples', 10_000_000), ('validationSamples', 100_000), ('microBatch', 64),
                           ('accumulation', 4096), ('loraRank', 128), ('loraAlpha', 512),
                           ('checkpointSteps', 100_000), ('maxTrainingTokens', 32768),
                           ('maxNewTokens', 8192), ('inferenceBatch', 64)]:
        require(type(cfg.get(name)) is int and 1 <= cfg[name] <= maximum, f'Invalid configuration: {name}')
    for name, maximum in [('learningRate', 0.01), ('warmupFraction', 1.0)]:
        value = cfg.get(name)
        require(type(value) in [int, float] and math.isfinite(value) and 0 < value <= maximum, f'Invalid configuration: {name}')
    require(type(cfg.get('gradientCheckpointing')) is bool, 'Expected gradientCheckpointing boolean')
    if cfg['model'] == 'Qwen/Qwen3-1.7B':
        require(cfg.get('hardwareProfile') == 'rtx3080-10gb' and cfg.get('memoryBudgetGiB') == 10,
                'Qwen3-1.7B requires the explicit RTX 3080 10 GiB profile')
        require(cfg['microBatch'] == 1 and cfg['inferenceBatch'] == 1 and cfg['gradientCheckpointing'],
                'Qwen3-1.7B on RTX 3080 requires microBatch/inferenceBatch 1 and gradient checkpointing')
        require(cfg['maxTrainingTokens'] <= 3584, 'Qwen3-1.7B RTX 3080 token budget exceeded')
    require(type(cfg.get('seed')) is int and 0 <= cfg['seed'] < 2**32, 'Invalid random seed')
    modules = cfg.get('targetModules')
    require(isinstance(modules, list) and bool(modules) and all(isinstance(m, str) and m for m in modules)
            and len(set(modules)) == len(modules), 'Invalid LoRA target modules')
    require(cfg.get('profile', 'factual-review') in ['factual-review', 'competency-review-v1', 'tactical-review-v1', 'tactical-review-v5', 'tactical-review-v52'], 'Unknown training profile')
    from ..runtime_qwen35.config import validate_config
    validate_config(cfg)
    return cfg


def inspect_experiment(experiment, model_cache, manifest):
    root, cache = Path(experiment).resolve(), Path(model_cache).resolve()
    cfg = load_config(root)
    if cfg.get('profile') in ('tactical-review-v1', 'tactical-review-v5', 'tactical-review-v52'):
        from ..hashing import file_hash
        receipt = parse_json((root/'assembly-audit.json').read_text(encoding='utf-8'))
        names = {'config.json','instructions.txt','experimental-authorization.json','development.jsonl',
                 'development-metadata.json','validation.jsonl','validation-metadata.json'}
        require(receipt.get('profile') == cfg.get('profile') and receipt.get('inputIsolationPassed') is True
                and receipt.get('samples') == cfg['samples'] and receipt.get('validationSamples') == cfg['validationSamples'],
                'Missing or invalid tactical assembly audit')
        require(set(receipt.get('preparedFiles',{})) == names
                and all(file_hash(root/name) == receipt['preparedFiles'][name] for name in names),
                'Tactical assembled files changed')
    require((root / 'instructions.txt').is_file() and (root / 'experimental-authorization.json').is_file(),
            'Missing instructions or experimental authorization record')
    authorization = parse_json((root / 'experimental-authorization.json').read_text(encoding='utf-8-sig'))
    require(isinstance(authorization, dict) and isinstance(authorization.get('scope'), str)
            and bool(authorization['scope'].strip()), 'Experimental authorization needs a scope')
    snapshot = cache / 'hub' / ('models--' + cfg['model'].replace('/', '--')) / 'snapshots' / cfg['revision']
    require(snapshot.is_dir() and snapshot.resolve().is_relative_to(cache), 'Pinned local model snapshot is missing or outside cache')
    profile = cfg.get('profile', 'factual-review')
    joint = audit_experiment(root, manifest, profile)
    require(joint['inputIsolationPassed'], 'Duplicate model inputs within or across training/validation partitions')
    reports = joint['data']
    for name, partition, count in [('development', 'train', cfg['samples']), ('validation', 'validation', cfg['validationSamples'])]:
        require(reports[partition]['rows'] == count, f'Configured sample count differs from {name} data')
    return {'model': cfg['model'], 'revision': cfg['revision'], 'snapshot': str(snapshot), 'profile': profile,
            'data': reports, 'inputIsolationPassed': True, 'gpuInitialized': False, 'optimizerUpdated': False}
