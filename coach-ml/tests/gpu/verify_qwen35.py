"""Explicit night-only qualification: real BF16 weights, AdamW allocation, fused loss and optimizer resume.

This is synthetic technical validation, never corpus training or tactical approval.
"""
import argparse
import gc
import json
import math
import os
from pathlib import Path
import random
import subprocess
import threading
import time
from unittest.mock import patch


class MemorySamples:
    def __init__(self):
        self.samples, self.errors, self.stop = [], [], threading.Event()
        self.thread = threading.Thread(target=self.collect, daemon=True)

    def collect(self):
        while not self.stop.is_set():
            try:
                text = subprocess.check_output(['nvidia-smi', '--id=0', '--query-gpu=memory.total,memory.used',
                                                '--format=csv,noheader,nounits'], text=True, timeout=5)
                total, used = map(int, text.strip().split(','))
                self.samples.append({'seconds': time.monotonic(), 'totalMiB': total, 'usedMiB': used})
            except Exception as error:
                self.errors.append(str(error))
            self.stop.wait(.1)


def fused_loss_parity(torch):
    from unsloth_zoo.loss_utils import fused_linear_cross_entropy
    e = (torch.randn(1, 8, 16, device='cuda', dtype=torch.bfloat16) * .1).requires_grad_()
    c = (torch.randn(32, 16, device='cuda', dtype=torch.bfloat16) * .1).requires_grad_()
    targets = torch.tensor([[-100, -100, -100, -100, 3, 4, 5, 6]], device='cuda')
    fused = fused_linear_cross_entropy(e, c, targets)
    actual = torch.autograd.grad(fused, (e, c))
    logits = torch.nn.functional.linear(e, c)
    reference = torch.nn.functional.cross_entropy(logits[:, :-1].float().reshape(-1, 32),
                                                 targets[:, 1:].reshape(-1), ignore_index=-100)
    expected = torch.autograd.grad(reference, (e, c))
    # Fixed before running: BF16 math and the backend's documented gradient filtering are approximate.
    torch.testing.assert_close(fused.float(), reference.float(), rtol=.02, atol=.02)
    for a, b in zip(actual, expected):
        torch.testing.assert_close(a.float(), b.float(), rtol=.02, atol=.02)
    return {'passed': True, 'rtol': .02, 'atol': .02, 'referenceLoss': float(reference), 'fusedLoss': float(fused)}


def full_length_step(runtime, snapshot, cfg, torch, root):
    model = runtime.attach(runtime.load_base(snapshot, cfg), cfg)
    model.train()
    ids = torch.randint(10, 1000, (1, cfg['maxTrainingTokens']), device='cuda')
    labels = ids.clone()
    labels[:, :cfg['maxTrainingTokens'] // 2] = -100
    parameters = [p for p in model.parameters() if p.requires_grad]
    optimizer = torch.optim.AdamW(parameters, lr=cfg['learningRate'], weight_decay=0)
    output = model(input_ids=ids, attention_mask=torch.ones_like(ids), labels=labels)
    assert torch.isfinite(output.loss)
    logits = getattr(output, 'logits', None)
    assert not isinstance(logits, torch.Tensor) or logits.numel() == 0, 'Training materialized full vocabulary logits'
    output.loss.backward()
    norm = torch.nn.utils.clip_grad_norm_(parameters, 1.)
    assert torch.isfinite(norm)
    optimizer.step()  # Include the lazy allocation of actual AdamW states in the measured peak.
    model.save_pretrained(root / 'step-adapter', safe_serialization=True)
    model.eval()
    model.config.use_cache = True
    with torch.no_grad():
        result = model.generate(input_ids=ids, attention_mask=torch.ones_like(ids), do_sample=False,
                                max_new_tokens=cfg['maxNewTokens'])
    assert result.shape[1] > ids.shape[1]
    del model, optimizer, parameters, output, ids, labels, logits, norm, result
    gc.collect()
    torch.cuda.empty_cache()


def optimizer_run(root, runtime, snapshot, cfg, torch, pause_step=None):
    from coach_ml.runtime import optimization
    from coach_ml.checkpoints import seal_checkpoint, verify_checkpoint
    from coach_ml.hashing import file_hash
    from coach_ml.experiments.control import TrainingPaused
    root.mkdir()
    output = root / 'output'
    output.mkdir()
    (root / 'freeze.json').write_text(json.dumps({'syntheticQualification': True, 'config': cfg}))
    train = [([11, 12, 13 + i], [21, 22, 23]) for i in range(5)]
    def batch(rows):
        return {'input_ids': torch.tensor([p + t for p, t in rows], device='cuda'),
                'labels': torch.tensor([[-100] * len(p) + t for p, t in rows], device='cuda')}
    def reference_loss(model, rows):
        model.eval()
        with torch.no_grad():
            return {'loss': float(model(**batch(rows)).loss)}
    def atomic_json(path, value):
        temporary = path.with_suffix('.tmp')
        temporary.write_text(json.dumps(value))
        temporary.replace(path)
    fingerprint = lambda: runtime.identity(snapshot, cfg)
    frozen = {'preflight': {'optimizerSteps': 10}, 'fingerprint': fingerprint()}
    env = dict(output=output, time=time, train=train, random=random, cfg=cfg,
               load_base=lambda: runtime.load_base(snapshot, cfg), reference_loss=reference_loss,
               validation=train[:1], attach=lambda model: runtime.attach(model, cfg), torch=torch, json=json,
               verify_checkpoint=verify_checkpoint, sha=file_hash, ROOT=root, frozen=frozen, math=math,
               batch=batch, atomic_json=atomic_json, seal_checkpoint=seal_checkpoint,
               fingerprint=fingerprint, gc=gc)
    real_pause, calls = optimization.pause_requested, 0
    def pause():
        nonlocal calls
        calls += 1
        return calls == pause_step or real_pause()
    with patch.object(optimization, 'pause_requested', pause):
        try:
            optimization.train_epoch(**env)
        except TrainingPaused:
            if real_pause():
                raise  # Never override an actual operator pause with the synthetic resume exercise.
            assert pause_step == 6
            pointer = json.loads((output / 'latest-checkpoint.json').read_text())
            assert (pointer['epoch'], pointer['cursorInEpoch']) == (1, 1)
            gc.collect()
            torch.cuda.empty_cache()
            # Disable only the synthetic pause; the real shared-store pause remains active.
            with patch.object(optimization, 'pause_requested', real_pause):
                optimization.train_epoch(**env)
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--snapshot', type=Path, required=True)
    parser.add_argument('--config', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    assert os.environ.get('COACH_ML_QWEN35_GPU_TEST') == '1', 'Explicit night-time GPU test opt-in required'
    assert not args.output.exists(), 'Qualification output must be new'
    from coach_ml.experiments.preparation import load_config
    from coach_ml.experiments.runs import RunStore
    from coach_ml.core.json import digest
    from coach_ml.runtime_qwen35.training import environment_identity
    assert os.environ.get('COACH_ML_RUN_STORE'), 'Use the same shared GPU run store as all other workers'
    cfg = json.loads(args.config.read_text(encoding='utf-8-sig'))
    # Validate through the normal config contract before any tensor import or resource acquisition.
    import tempfile
    with tempfile.TemporaryDirectory() as temporary:
        path = Path(temporary)
        (path / 'config.json').write_text(json.dumps(cfg))
        cfg = load_config(path)
    environment_identity()
    store = RunStore(os.environ['COACH_ML_RUN_STORE'])
    args.output.mkdir()
    run_id = digest({'qualification': str(args.output.resolve())})
    attempt = store.start(run_id, args.output, digest(cfg))
    os.environ.update(COACH_ML_RUN_ID=run_id, COACH_ML_ATTEMPT=attempt)
    sampler = MemorySamples()
    sampler.thread.start()
    result = {'gpuQualified': False, 'tacticalQualityApproved': False, 'syntheticQualification': True}
    try:
        os.environ.update(HF_HUB_OFFLINE='1', TRANSFORMERS_OFFLINE='1', UNSLOTH_RETURN_LOGITS='0',
                          CUBLAS_WORKSPACE_CONFIG=':4096:8')
        from coach_ml.runtime_qwen35.model import verify_snapshot, validate_text_layout
        verify_snapshot(args.snapshot, weights=True)
        import torch
        from unsloth import FastLanguageModel
        from transformers.models.qwen3_5 import modeling_qwen3_5
        from coach_ml.runtime_qwen35.training import Qwen35Runtime
        assert torch.cuda.is_available() and torch.cuda.is_bf16_supported()
        assert modeling_qwen3_5.causal_conv1d_fn is not None and modeling_qwen3_5.chunk_gated_delta_rule is not None
        torch.use_deterministic_algorithms(True)
        torch.manual_seed(cfg['seed'])
        runtime = Qwen35Runtime(FastLanguageModel, torch, validate_text_layout(args.snapshot))
        result['lossParity'] = fused_loss_parity(torch)
        torch.cuda.reset_peak_memory_stats()
        full_length_step(runtime, args.snapshot, cfg, torch, args.output)
        reserved = torch.cuda.max_memory_reserved()
        allocated = torch.cuda.max_memory_allocated()
        test_cfg = {**cfg, 'epochs': 2, 'microBatch': 1, 'accumulation': 1, 'checkpointSteps': 1}
        full = optimizer_run(args.output / 'continuous', runtime, args.snapshot, test_cfg, torch)
        resumed = optimizer_run(args.output / 'resumed', runtime, args.snapshot, test_cfg, torch, pause_step=6)
        from safetensors.torch import load_file
        a = load_file(full / 'adapter-final/adapter_model.safetensors')
        b = load_file(resumed / 'adapter-final/adapter_model.safetensors')
        assert a.keys() == b.keys() and all(torch.equal(a[key], b[key]) for key in a), 'Resume must match exactly'
        assert (full / 'effective-visit-order.json').read_bytes() == (resumed / 'effective-visit-order.json').read_bytes()
        torch.cuda.synchronize()
        assert sampler.samples and not sampler.errors
        peak = max(sample['usedMiB'] for sample in sampler.samples)
        assert all(sample['totalMiB'] == 10240 for sample in sampler.samples), 'This profile targets 10 GiB'
        assert peak <= 9728 and sampler.samples[0]['usedMiB'] + math.ceil(reserved / 1024**2) <= 9728, 'No 512 MiB margin'
        result.update(gpuQualified=True, resumeExact=True, optimizerStepMeasured=True,
                      peakDeviceUsedMiB=peak, fullLengthPeakReservedBytes=reserved, fullLengthPeakAllocatedBytes=allocated,
                      maxSamplingGapSeconds=max(b['seconds'] - a['seconds'] for a, b in zip(sampler.samples, sampler.samples[1:])))
        assert result['maxSamplingGapSeconds'] <= 1, 'Memory sampling gaps exceed qualification limit'
        store.transition(run_id, attempt, 'completed')
    except BaseException as error:
        result.update(gpuQualified=False, error=str(error))
        from coach_ml.experiments.control import TrainingPaused
        if isinstance(error, TrainingPaused):
            pointers = list(args.output.glob('*/output/latest-checkpoint.json'))
            pointer = max(pointers, key=lambda p: p.stat().st_mtime)
            checkpoint = json.loads(pointer.read_text())
            from coach_ml.checkpoints import verify_checkpoint
            from coach_ml.hashing import file_hash
            verify_checkpoint(pointer.parent, checkpoint, file_hash(pointer.parents[1] / 'freeze.json'))
            store.transition(run_id, attempt, 'paused', checkpoint={
                'subrun': str(pointer.parents[1].relative_to(args.output)), 'checkpoint': checkpoint})
        else:
            store.transition(run_id, attempt, 'failed', detail='Inspect local qualification evidence')
        raise
    finally:
        sampler.stop.set()
        sampler.thread.join(timeout=6)
        result.update(memorySamples=sampler.samples, samplingErrors=sampler.errors)
        (args.output / 'qualification.json').write_text(json.dumps(result, indent=2) + '\n')


if __name__ == '__main__':
    main()
