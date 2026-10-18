"""Bounded real-CUDA parity of extracted optimization against the original phase."""
import ast
import gc
import inspect
import json
import math
import os
from pathlib import Path
import random
import tempfile
import time
from unittest.mock import patch

assert os.environ.get('COACH_ML_GPU_TEST') == '1', 'Explicit GPU test opt-in required'
import torch
from peft import LoraConfig, get_peft_model
from safetensors.torch import load_file
from transformers import LlamaConfig, LlamaForCausalLM
from coach_ml.runtime.optimization import train_epoch
from coach_ml.experiments.control import TrainingPaused
from coach_ml.checkpoints import seal_checkpoint, verify_checkpoint
from coach_ml.hashing import file_hash

assert torch.cuda.is_available()
free, total = torch.cuda.mem_get_info()
assert free > 512 * 1024**2, 'Not enough free GPU memory for bounded synthetic parity'
torch.cuda.set_per_process_memory_fraction(min(0.025, 256 * 1024**2 / total))
torch.set_num_threads(2)
torch.use_deterministic_algorithms(True)
torch.cuda.reset_peak_memory_stats()
from fixtures.optimization_before_split import train_epoch as legacy_train_epoch

def run(root, function, pause=False):
    root.mkdir(exist_ok=True)
    output = root / 'output'
    output.mkdir(exist_ok=True)
    (root / 'freeze.json').write_text('{}')
    cfg = dict(seed=42, microBatch=1, accumulation=1, learningRate=0.001, warmupFraction=0.25, checkpointSteps=1)
    train = [([1, 3, 5], [7, 2]), ([1, 4, 6], [8, 2]), ([1, 5, 7], [9, 2]), ([1, 6, 8], [10, 2])]
    validation = train[:1]
    def load_base():
        torch.manual_seed(100)
        config = LlamaConfig(vocab_size=32, hidden_size=16, intermediate_size=32,
                             num_hidden_layers=1, num_attention_heads=2, num_key_value_heads=1,
                             max_position_embeddings=32, attention_dropout=0)
        return LlamaForCausalLM(config).to(device='cuda', dtype=torch.bfloat16)
    def attach(model):
        return get_peft_model(model, LoraConfig(r=2, lora_alpha=4, lora_dropout=0,
                              target_modules=['q_proj', 'v_proj'], task_type='CAUSAL_LM'))
    def batch(rows):
        return {'input_ids': torch.tensor([p+t for p,t in rows], device='cuda'),
                'labels': torch.tensor([[-100]*len(p)+t for p,t in rows], device='cuda')}
    def reference_loss(model, rows):
        model.eval()
        with torch.no_grad():
            return {'loss': float(model(**batch(rows)).loss)}
    def atomic_json(path, value):
        temp = path.with_suffix('.tmp')
        temp.write_text(json.dumps(value))
        os.replace(temp, path)
    frozen = {'preflight': {'optimizerSteps': 4}, 'fingerprint': {'synthetic': True}}
    env = dict(output=output, time=time, train=train, random=random, cfg=cfg, load_base=load_base,
               reference_loss=reference_loss, validation=validation, attach=attach, torch=torch,
               json=json, verify_checkpoint=verify_checkpoint, sha=file_hash, ROOT=root,
               frozen=frozen, math=math, batch=batch, atomic_json=atomic_json,
               seal_checkpoint=seal_checkpoint, fingerprint=lambda: {'synthetic': True}, gc=gc)
    calls = 0
    def should_pause():
        nonlocal calls
        calls += 1
        return pause and calls == 2
    with patch('coach_ml.runtime.optimization.pause_requested', should_pause):
        try:
            function(**env)
        except TrainingPaused:
            assert pause
            checkpoint = json.loads((output/'latest-checkpoint.json').read_text())
            assert checkpoint['step'] == 2
            verify_checkpoint(output, checkpoint, file_hash(root/'freeze.json'))
            return False
    return True

with tempfile.TemporaryDirectory() as folder:
    root = Path(folder)
    assert run(root/'legacy', legacy_train_epoch)
    assert not run(root/'resumed', train_epoch, pause=True)
    assert run(root/'resumed', train_epoch)
    legacy_weights = load_file(root/'legacy/output/adapter-final/adapter_model.safetensors')
    resumed_weights = load_file(root/'resumed/output/adapter-final/adapter_model.safetensors')
    assert legacy_weights.keys() == resumed_weights.keys()
    assert all(torch.equal(legacy_weights[key], resumed_weights[key]) for key in legacy_weights)
    assert json.loads((root/'legacy/output/effective-visit-order.json').read_text()) == json.loads((root/'resumed/output/effective-visit-order.json').read_text())
    result = {'gpuParityPassed': True, 'exactAdapterTensors': len(legacy_weights), 'steps': 4,
              'interruptedAfterStep': 2, 'peakAllocatedBytes': torch.cuda.max_memory_allocated(),
              'productionTrainingTouched': False, 'syntheticModel': True, 'baseDtype': 'bfloat16'}
    with Path(os.environ['COACH_ML_GPU_EVIDENCE']).open('x', encoding='utf-8') as report:
        json.dump(result, report, indent=2)
    print(json.dumps(result))
