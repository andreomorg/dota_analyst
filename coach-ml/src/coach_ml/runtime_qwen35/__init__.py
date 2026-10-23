"""Opt-in Qwen3.5 runtime. Importing this package never initializes tensor libraries."""


def run(stage, experiment, model_cache):
    from .training import run as run_training
    return run_training(stage, experiment, model_cache)
