"""Composition adapter; importing it never initializes PyTorch or CUDA."""
def run_lora(stage, experiment, model_cache):
    from ..lora import run
    return run(stage, experiment, model_cache)
