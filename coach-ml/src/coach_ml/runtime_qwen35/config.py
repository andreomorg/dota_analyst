"""A fixed, unqualified hardware candidate; config validation has no tensor imports."""
from ..core.validation import require

RUNTIME = 'qwen35-text-bf16-v1'
MODEL = 'Qwen/Qwen3.5-4B'
REVISION = '851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a'
TARGETS = ('q_proj', 'k_proj', 'v_proj', 'o_proj', 'in_proj_qkv', 'in_proj_z', 'out_proj')
VERSIONS = {'torch': '2.8.0', 'transformers': '5.17.0', 'unsloth': '2026.10.8',
            'unsloth_zoo': '2026.10.8', 'peft': '0.21.2', 'triton': '3.4.0',
            'flash-linear-attention': '0.5.2', 'fla-core': '0.5.2', 'causal-conv1d': '1.7.0',
            'xformers': '0.0.32.post2', 'torchao': '0.13.0', 'accelerate': '1.10.1',
            'safetensors': '0.8.0', 'tokenizers': '0.23.3'}


def validate_config(cfg):
    runtime = cfg.get('runtime')
    if runtime is None:
        require(not cfg['model'].startswith('Qwen/Qwen3.5-'), 'Qwen3.5 requires an explicit isolated runtime')
        return
    require(runtime == RUNTIME, 'Unknown training runtime')
    require(cfg['model'] == MODEL and cfg['revision'] == REVISION, 'Unexpected Qwen3.5 checkpoint')
    require(cfg.get('profile') == 'tactical-review-v52', 'Qwen3.5 runtime requires tactical-review-v52')
    require(cfg.get('precision') == 'bf16' and cfg.get('textOnly') is True
            and cfg.get('quantization') is None, 'Qwen3.5 runtime requires unquantized BF16 text')
    require(cfg.get('hardwareProfile') == 'rtx3080-10gb' and cfg.get('memoryBudgetGiB') == 10,
            'Expected explicit RTX 3080 memory profile')
    require(cfg['microBatch'] == cfg['inferenceBatch'] == 1 and cfg['gradientCheckpointing'],
            'Qwen3.5 requires batch 1 and gradient checkpointing')
    require(cfg['maxTrainingTokens'] <= 3584 and cfg['maxNewTokens'] <= 512, 'Qwen3.5 token budget exceeded')
    require(cfg['loraRank'] == 16 and cfg['loraAlpha'] == 32 and set(cfg['targetModules']) == set(TARGETS),
            'Unexpected Qwen3.5 adapter recipe')
    require(type(cfg.get('offloadEmbedding')) is bool, 'Explicit embedding offload policy required')
