"""Inject Qwen3.5 ports into the already-tested experiment phases, without a second optimizer loop."""
from importlib import metadata
import os
from pathlib import Path
import re

from ..core.validation import require
from ..hashing import file_hash
from .config import RUNTIME, VERSIONS, validate_config
from .encoding import encode_example
from .model import verify_snapshot, validate_text_layout, load_text_model


def environment_identity():
    image = os.environ.get('COACH_ML_RUNTIME_IMAGE', '')
    require(re.fullmatch(r'sha256:[0-9a-f]{64}', image), 'Qwen3.5 requires the immutable runtime image ID')
    installed = {name: metadata.version(name) for name in VERSIONS}
    require(all(installed[name].split('+')[0] == version for name, version in VERSIONS.items()),
            'Isolated Qwen3.5 runtime package versions changed')
    return {'imageId': image, 'packages': installed}


class Qwen35Runtime:
    def __init__(self, framework, torch, layout):
        self.framework, self.torch, self.layout = framework, torch, layout

    @staticmethod
    def tokenizer(snapshot, cfg):
        from transformers import AutoTokenizer
        return AutoTokenizer.from_pretrained(snapshot, local_files_only=True, trust_remote_code=False)

    @staticmethod
    def encode(messages, tokenizer, cfg):
        return encode_example(messages, tokenizer, cfg)

    def identity(self, snapshot, cfg):
        return {'id': RUNTIME, **environment_identity(), 'precision': 'bf16', 'textOnly': True,
                'targetModules': cfg['targetModules'], 'checkpointing': 'unsloth',
                'offloadEmbedding': cfg['offloadEmbedding'], 'layout': self.layout,
                'deterministicTorchAlgorithms': True,
                'tokenizerFiles': {name: file_hash(Path(snapshot) / name) for name in
                                   ('tokenizer.json', 'tokenizer_config.json', 'chat_template.jinja')},
                'memoryQualification': 'external-preflight-required',
                'lossBackend': 'unsloth-fused-linear-cross-entropy'}

    def load_base(self, snapshot, cfg):
        model, _ = load_text_model(snapshot, cfg, self.framework, self.torch)
        return model

    def attach(self, model, cfg):
        model = self.framework.get_peft_model(
            model, r=cfg['loraRank'], lora_alpha=cfg['loraAlpha'], lora_dropout=0,
            target_modules=cfg['targetModules'], bias='none', use_gradient_checkpointing='unsloth',
            random_state=cfg['seed'])
        trained = [name for name, parameter in model.named_parameters() if parameter.requires_grad]
        require(trained and all('lora_' in name and not any(part in name.split('.') for part in ('visual', 'mtp'))
                                for name in trained), 'Only textual LoRA parameters may be trainable')
        return model


def run(stage, experiment, model_cache):
    from ..experiments.preparation import load_config
    cfg = load_config(experiment)
    validate_config(cfg)
    require(stage in ('prepare', 'run'), 'Qwen3.5 tensor runtime only supports prepare/run')
    environment_identity()
    require(os.environ.get('COACH_ML_RUN_STORE'), 'Qwen3.5 requires the shared GPU run store')
    snapshot = Path(model_cache) / 'hub' / 'models--Qwen--Qwen3.5-4B' / 'snapshots' / cfg['revision']
    verify_snapshot(snapshot, weights=True)
    os.environ['HF_HUB_OFFLINE'] = '1'
    os.environ['TRANSFORMERS_OFFLINE'] = '1'
    os.environ.setdefault('CUBLAS_WORKSPACE_CONFIG', ':4096:8')
    os.environ['UNSLOTH_RETURN_LOGITS'] = '0'
    import torch
    require(torch.cuda.is_available() and torch.cuda.is_bf16_supported(), 'Qwen3.5 BF16 requires an authorized CUDA run')
    # Unsloth must patch before Transformers and PEFT are imported by the generic phase composition.
    from unsloth import FastLanguageModel
    from transformers.models.qwen3_5 import modeling_qwen3_5
    require(modeling_qwen3_5.causal_conv1d_fn is not None
            and modeling_qwen3_5.chunk_gated_delta_rule is not None, 'Required Qwen3.5 fast kernels unavailable')
    torch.use_deterministic_algorithms(True)
    runtime = Qwen35Runtime(FastLanguageModel, torch, validate_text_layout(snapshot))
    from ..lora import run as run_phases
    return run_phases(stage, experiment, model_cache, runtime=runtime)
