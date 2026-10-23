"""Small injected tensor/optimizer port: no ML imports, network or hardware."""
import gc
import json
import math
import pickle
import random
import time
from types import SimpleNamespace

from coach_ml.checkpoints import verify_checkpoint, seal_checkpoint
from coach_ml.hashing import file_hash


class Scalar:
    requires_grad = True
    def __init__(self, value): self.value = value
    def detach(self): return self
    def cpu(self): return self
    def clone(self): return Scalar(self.value)
    def __float__(self): return float(self.value)
    def __sub__(self, other): return Scalar(self.value - other.value)
    def __truediv__(self, other): return Scalar(self.value / other)
    def abs(self): return Scalar(abs(self.value))
    def sum(self): return self
    def backward(self): pass


class Model:
    def __init__(self): self.param = Scalar(1)
    def parameters(self): return [self.param]
    def named_parameters(self): return [('lora_test', self.param)]
    def train(self): pass
    def __call__(self, **batch): return SimpleNamespace(loss=Scalar(sum(batch['rows']) + 1))
    def save_pretrained(self, folder, **kwargs):
        folder.mkdir(exist_ok=True)
        (folder / 'adapter_config.json').write_text('{}')
        (folder / 'adapter_model.safetensors').write_text(json.dumps(self.param.value))


class Optimizer:
    def __init__(self, params, lr, **kwargs):
        self.params, self.param_groups, self.steps = params, [{'lr': lr}], 0
    def zero_grad(self, **kwargs): pass
    def step(self):
        self.steps += 1
        self.params[0].value += self.param_groups[0]['lr']
    def state_dict(self): return {'steps': self.steps, 'groups': self.param_groups}
    def load_state_dict(self, state): self.steps, self.param_groups = state['steps'], state['groups']


def environment(root, epochs=1):
    root.mkdir(exist_ok=True)
    output = root / 'output'
    output.mkdir(exist_ok=True)
    cfg = dict(epochs=epochs, seed=42, microBatch=2, accumulation=2, learningRate=.01,
               warmupFraction=.25, checkpointSteps=1)
    (root / 'freeze.json').write_text(json.dumps({'config': cfg}))
    def save(value, path): path.write_bytes(pickle.dumps(value))
    def load(path, **kwargs): return pickle.loads(path.read_bytes())
    def atomic_json(path, value): path.write_text(json.dumps(value))
    cuda = SimpleNamespace(reset_peak_memory_stats=lambda: None, empty_cache=lambda: None,
                           max_memory_allocated=lambda: 0, get_rng_state_all=lambda: [],
                           set_rng_state_all=lambda value: None)
    torch = SimpleNamespace(optim=SimpleNamespace(AdamW=Optimizer), cuda=cuda,
                            nn=SimpleNamespace(utils=SimpleNamespace(clip_grad_norm_=lambda *a: Scalar(1))),
                            isfinite=lambda value: True, save=save, load=load,
                            get_rng_state=lambda: 0, set_rng_state=lambda value: None)
    frozen = {'preflight': {'optimizerSteps': 2 * epochs}, 'fingerprint': {'fake': True}}
    env = dict(output=output, time=time, train=list(range(5)), random=random, cfg=cfg,
               load_base=Model, reference_loss=lambda *args: {'loss': 1}, validation=[0],
               attach=lambda model: model, torch=torch, json=json, verify_checkpoint=verify_checkpoint,
               sha=file_hash, ROOT=root, frozen=frozen, math=math, batch=lambda rows: {'rows': rows},
               atomic_json=atomic_json, seal_checkpoint=seal_checkpoint,
               fingerprint=lambda: {'fake': True}, gc=gc)
    return env


def resume_modules():
    def load_file(path): return json.loads(path.read_text())
    def restore(model, value): model.param.value = value
    return {'peft': SimpleNamespace(set_peft_model_state_dict=restore),
            'safetensors': SimpleNamespace(), 'safetensors.torch': SimpleNamespace(load_file=load_file)}
