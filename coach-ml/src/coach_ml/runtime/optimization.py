"""Extracted phase; tensor dependencies are supplied by the composition runtime."""
from .checkpointing import save_training_checkpoint
from ..experiments.control import pause_requested, TrainingPaused

def train_epoch(output, time, train, random, cfg, load_base, reference_loss, validation, attach, torch, json, verify_checkpoint, sha, ROOT, frozen, math, batch, atomic_json, seal_checkpoint, fingerprint, gc):
    if (output / 'training-result.json').exists():
        return
    started = time.perf_counter()
    order = list(range(len(train)))
    random.Random(cfg['seed']).shuffle(order)
    assert len(set(order)) == len(train)
    model = load_base()
    base_loss = reference_loss(model, validation)
    model = attach(model)
    params = [p for p in model.parameters() if p.requires_grad]
    initial = {n: p.detach().cpu().clone() for n, p in model.named_parameters() if p.requires_grad}
    optimizer = torch.optim.AdamW(params, lr=cfg['learningRate'], weight_decay=0)
    cursor, step, elapsed_prior = (0, 0, 0.0)
    latest = output / 'latest-checkpoint.json'
    if latest.exists():
        checkpoint = json.loads(latest.read_text(encoding='utf-8-sig'))
        folder = verify_checkpoint(output, checkpoint, sha(ROOT / 'freeze.json'))
        from peft import set_peft_model_state_dict
        from safetensors.torch import load_file
        set_peft_model_state_dict(model, load_file(folder / 'adapter_model.safetensors'))
        state = torch.load(folder / 'state.pt', map_location='cpu', weights_only=False)
        assert state['cursor'] == checkpoint['cursor'] and state['step'] == checkpoint['step'], 'Checkpoint state counters mismatch'
        optimizer.load_state_dict(state['optimizer'])
        cursor, step, elapsed_prior = (state['cursor'], state['step'], state['elapsedSeconds'])
        random.setstate(state['pythonRandom'])
        torch.set_rng_state(state['torchRandom'])
        torch.cuda.set_rng_state_all(state['cudaRandom'])
        print(json.dumps({'resumedCursor': cursor, 'step': step}), flush=True)
    total_steps = frozen['preflight']['optimizerSteps']
    warmup = max(1, math.ceil(total_steps * cfg['warmupFraction']))
    attempt = len(list(output.glob('attempt-*.jsonl'))) + 1
    log = output / f'attempt-{attempt:03}.jsonl'
    torch.cuda.reset_peak_memory_stats()
    model.train()
    while cursor < len(order):
        optimizer.zero_grad(set_to_none=True)
        indexes = order[cursor:cursor + cfg['microBatch'] * cfg['accumulation']]
        micros = [indexes[i:i + cfg['microBatch']] for i in range(0, len(indexes), cfg['microBatch'])]
        loss_sum = 0.0
        for micro in micros:
            loss = model(**batch([train[i] for i in micro])).loss
            assert bool(torch.isfinite(loss)), 'Nonfinite loss'
            (loss / len(micros)).backward()
            loss_sum += float(loss.detach())
        norm = torch.nn.utils.clip_grad_norm_(params, 1.0)
        assert bool(torch.isfinite(norm)), 'Nonfinite gradients'
        next_step = step + 1
        factor = next_step / warmup if next_step <= warmup else (total_steps - next_step + 1) / max(1, total_steps - warmup)
        for group in optimizer.param_groups:
            group['lr'] = cfg['learningRate'] * factor
        optimizer.step()
        cursor += len(indexes)
        step = next_step
        entry = {'step': step, 'cursor': cursor, 'rows': indexes, 'meanMicroLoss': loss_sum / len(micros), 'gradientNorm': float(norm), 'learningRate': optimizer.param_groups[0]['lr'], 'elapsedSeconds': elapsed_prior + time.perf_counter() - started}
        with log.open('a', encoding='utf-8') as stream:
            stream.write(json.dumps(entry) + '\n')
        atomic_json(ROOT / 'live-status.json', {'status': 'training', 'totalSamples': len(train), 'totalSteps': total_steps, **{k: v for k, v in entry.items() if k != 'rows'}})
        if step % 10 == 0 or cursor == len(order):
            print(json.dumps({k: v for k, v in entry.items() if k != 'rows'}), flush=True)
        requested = pause_requested()
        if step % cfg['checkpointSteps'] == 0 or cursor == len(order) or requested:
            save_training_checkpoint(step, output, model, torch, optimizer, cursor, entry, random, sha, ROOT, seal_checkpoint, verify_checkpoint, atomic_json, latest)
        if requested:
            del model, optimizer, params, loss, norm
            gc.collect()
            torch.cuda.empty_cache()
            raise TrainingPaused('Paused after a sealed optimizer-step checkpoint')
    assert cursor == len(train) and step == total_steps
    adapted_loss = reference_loss(model, validation)
    changed = [float((p.detach().cpu() - initial[n]).abs().sum()) for n, p in model.named_parameters() if n in initial]
    assert sum(changed) > 0
    model.save_pretrained(output / 'adapter-final', safe_serialization=True)
    atomic_json(output / 'effective-visit-order.json', order)
    assert fingerprint() == frozen['fingerprint'], 'Base/data/code mutated'
    result = {'samples': cursor, 'epochs': 1, 'steps': step, 'baseReference': base_loss, 'adaptedReference': adapted_loss, 'changedTensors': sum((x > 0 for x in changed)), 'adapterDeltaL1': sum(changed), 'peakAllocatedBytes': torch.cuda.max_memory_allocated(), 'elapsedSeconds': elapsed_prior + time.perf_counter() - started, 'baseWeightsUnchanged': True, 'testUsed': False, 'humanApproved': False, 'tacticalImprovementValidated': False}
    atomic_json(output / 'training-result.json', result)
    print(json.dumps(result), flush=True)
    del model, optimizer, params
    gc.collect()
    torch.cuda.empty_cache()
