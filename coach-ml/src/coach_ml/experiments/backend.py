"""Composition adapter; importing it never initializes PyTorch or CUDA."""
def run_lora(stage, experiment, model_cache):
    from .preparation import load_config
    cfg = load_config(experiment)
    if cfg.get('runtime') == 'qwen35-text-bf16-v1':
        from ..runtime_qwen35 import run
    else:
        from ..lora import run
    return run(stage, experiment, model_cache)
