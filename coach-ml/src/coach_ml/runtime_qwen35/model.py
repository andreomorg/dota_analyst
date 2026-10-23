"""Strict local checkpoint inspection and a text-only loader port."""
import json
from pathlib import Path
import struct

from ..core.json import digest
from ..core.validation import require
from ..hashing import file_hash
from .config import TARGETS, validate_config
from .artifacts import PINNED_FILES


def verify_snapshot(snapshot, *, weights=False, expected=None):
    snapshot = Path(snapshot)
    expected = PINNED_FILES if expected is None else expected
    for name, entry in expected.items():
        require(Path(name).name == name, 'Unsafe checkpoint artifact name')
        path = snapshot / name
        require(path.is_file() and path.stat().st_size == entry['bytes'], 'Missing or incomplete model file: ' + name)
        if weights or not name.endswith('.safetensors'):
            require(file_hash(path) == entry['sha256'], 'Pinned model hash mismatch: ' + name)


def checkpoint_shapes(snapshot):
    snapshot = Path(snapshot)
    index = json.loads((snapshot / 'model.safetensors.index.json').read_text())['weight_map']
    require(type(index) is dict and bool(index), 'Missing checkpoint weight index')
    headers = {}
    for filename in set(index.values()):
        require(type(filename) is str and Path(filename).name == filename and filename.endswith('.safetensors'),
                'Unsafe shard reference')
        with (snapshot / filename).open('rb') as stream:
            prefix = stream.read(8)
            require(len(prefix) == 8, 'Incomplete safetensors prefix')
            length = struct.unpack('<Q', prefix)[0]
            require(0 < length <= 16 * 1024 * 1024, 'Invalid safetensors header length')
            raw = stream.read(length)
            require(len(raw) == length, 'Incomplete safetensors header')
            headers[filename] = json.loads(raw)
    shapes, ignored = {}, []
    for source, filename in index.items():
        require(source in headers[filename], 'Indexed tensor missing from shard')
        tensor = headers[filename][source]
        if source.startswith(('model.visual.', 'mtp.')):
            ignored.append(source)
            continue
        name = ('model.' + source.removeprefix('model.language_model.')
                if source.startswith('model.language_model.') else source)
        require(name.startswith('model.') or name == 'lm_head.weight', 'Unknown checkpoint tensor: ' + source)
        require(name not in shapes and tensor.get('dtype') in ('BF16', 'F32'), 'Duplicate or non-BF16/FP32 text tensor')
        shape = tensor.get('shape')
        require(type(shape) is list and all(type(n) is int and n > 0 for n in shape), 'Invalid text tensor shape')
        shapes[name] = shape
    return shapes, ignored


def validate_text_layout(snapshot):
    # Only metadata tensors are constructed, even for the real 4B checkpoint.
    import torch
    from transformers import Qwen3_5ForCausalLM, Qwen3_5TextConfig
    raw = json.loads((Path(snapshot) / 'config.json').read_text())
    require(raw.get('model_type') == 'qwen3_5', 'Expected Qwen3.5 composite checkpoint')
    config = Qwen3_5TextConfig(**raw['text_config'])
    with torch.device('meta'):
        reference = Qwen3_5ForCausalLM(config)
    expected = {name: list(tensor.shape) for name, tensor in reference.state_dict().items()}
    actual, ignored = checkpoint_shapes(snapshot)
    missing = set(expected) - set(actual)
    if config.tie_word_embeddings and missing == {'lm_head.weight'} and 'model.embed_tokens.weight' in actual:
        actual['lm_head.weight'] = actual['model.embed_tokens.weight']
    require(set(actual) == set(expected), 'Text weight coverage mismatch (missing or unexpected weights)')
    require(actual == expected, 'Text checkpoint shape mismatch')
    counts = {target: sum(name.endswith('.' + target + '.weight') for name in actual) for target in TARGETS}
    require(all(counts.values()), 'Checkpoint is missing a LoRA projection family')
    return {'textTensors': len(actual), 'ignoredTensors': len(ignored), 'targetCounts': counts,
            'layoutSha256': digest(actual), 'visionLoaded': False}


def validate_loaded_model(model, cfg):
    require(model.config.model_type == 'qwen3_5_text', 'Loader did not produce the text-only backbone')
    modules = dict(model.named_modules())
    require(not any(any(part in name.split('.') for part in ('visual', 'vision_model', 'mtp')) for name in modules),
            'Loader included vision or MTP modules')
    require(not any(parameter.is_meta for parameter in model.parameters()), 'Loader left uninitialized meta weights')
    require(all(any(name.endswith('.' + target) for name in modules) for target in cfg['targetModules']),
            'Loader is missing requested LoRA modules')


def load_text_model(snapshot, cfg, framework, torch):
    validate_config(cfg)
    model, tokenizer = framework.from_pretrained(
        model_name=str(snapshot), max_seq_length=cfg['maxTrainingTokens'] + cfg['maxNewTokens'], dtype=torch.bfloat16,
        load_in_4bit=False, load_in_8bit=False, load_in_16bit=True, full_finetuning=False,
        text_only=True, fast_inference=False, trust_remote_code=False, local_files_only=True,
        use_exact_model_name=True, use_gradient_checkpointing='unsloth',
        offload_embedding=cfg['offloadEmbedding'], random_state=cfg['seed'], use_kernels=False)
    validate_loaded_model(model, cfg)
    model.config.use_cache = False
    return model, tokenizer
