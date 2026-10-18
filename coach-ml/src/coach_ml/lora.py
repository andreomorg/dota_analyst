"""Versioned LoRA runtime for new offline experiments; legacy runs retain their frozen code."""

def run(stage, experiment, model_cache):
    import gc
    import json
    import math
    import os
    from pathlib import Path
    import random
    import time

    import torch
    from peft import LoraConfig, PeftModel, get_peft_model
    from transformers import AutoModelForCausalLM, AutoTokenizer
    from .environment import runtime_environment
    from .hashing import file_hash
    from .checkpoints import seal_checkpoint, verify_checkpoint
    from .predictions import prediction_prefix

    ROOT = Path(experiment)
    cfg = json.loads((ROOT / 'config.json').read_text(encoding='utf-8-sig'))
    CACHE = Path(model_cache) / 'hub' / ('models--' + cfg['model'].replace('/', '--')) / 'snapshots'
    snapshot = CACHE / cfg['revision']
    assert torch.cuda.is_available() and torch.cuda.is_bf16_supported()
    torch.set_num_threads(4)
    random.seed(cfg['seed'])
    torch.manual_seed(cfg['seed'])
    torch.cuda.manual_seed_all(cfg['seed'])
    tokenizer = AutoTokenizer.from_pretrained(snapshot, local_files_only=True, trust_remote_code=False)
    dtype = torch.bfloat16
    output = ROOT / 'output'
    output.mkdir(exist_ok=True)


    def sha(p):
        return file_hash(p)


    def atomic_json(file, value):
        temp = file.with_suffix(file.suffix + '.tmp')
        temp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
        os.replace(temp, file)


    def fingerprint():
        names = ['config.json', 'instructions.txt', 'development.jsonl', 'validation.jsonl',
                 'development-metadata.json', 'validation-metadata.json', 'experimental-authorization.json']
        weights = sorted(snapshot.glob('*.safetensors'))
        assert weights
        return {'files': {n: sha(ROOT / n) for n in names},
                'baseWeights': {p.name: sha(p) for p in weights}, 'revision': cfg['revision'],
                'runtimeCode': {p.name: sha(p) for p in Path(__file__).parent.glob('*.py')},
                'environment': runtime_environment()}


    def encode_split(name):
        encoded = []
        with (ROOT / (name + '.jsonl')).open(encoding='utf-8-sig') as stream:
            for index, line in enumerate(stream):
                messages = json.loads(line)['messages']
                prompt = tokenizer.apply_chat_template(messages[:2], tokenize=False, add_generation_prompt=True, enable_thinking=False)
                prefix = tokenizer(prompt, add_special_tokens=False)['input_ids']
                target = tokenizer(messages[2]['content'] + tokenizer.eos_token, add_special_tokens=False)['input_ids']
                assert prefix and target and len(prefix) + len(target) <= cfg['maxTrainingTokens'], f'Truncation forbidden: {name} row {index} tokens {len(prefix)+len(target)}'
                encoded.append((prefix, target))
                if (index + 1) % 5000 == 0:
                    atomic_json(ROOT / 'live-status.json', {'status': 'encoding', 'split': name, 'encoded': index + 1, 'optimizerUpdated': False})
                    print(json.dumps({'encodingSplit': name, 'encoded': index + 1}), flush=True)
        return encoded


    def load_base():
        model = AutoModelForCausalLM.from_pretrained(snapshot, local_files_only=True, trust_remote_code=False,
            dtype=dtype, attn_implementation='sdpa').to('cuda')
        model.config.use_cache = False
        return model


    def attach(model):
        model = get_peft_model(model, LoraConfig(r=cfg['loraRank'], lora_alpha=cfg['loraAlpha'],
            lora_dropout=0, target_modules=cfg['targetModules'], bias='none', task_type='CAUSAL_LM'))
        if cfg['gradientCheckpointing']:
            model.gradient_checkpointing_enable(gradient_checkpointing_kwargs={'use_reentrant': False})
            model.enable_input_require_grads()
        return model


    def batch(rows):
        torch.cuda.empty_cache()
        width = max(len(p) + len(t) for p, t in rows)
        ids, labels, masks = [], [], []
        for prefix, target in rows:
            pad = width - len(prefix) - len(target)
            ids.append(prefix + target + [tokenizer.eos_token_id] * pad)
            labels.append([-100] * len(prefix) + target + [-100] * pad)
            masks.append([1] * (len(prefix) + len(target)) + [0] * pad)
            assert labels[-1][:len(prefix)] == [-100] * len(prefix)
            assert sum(x != -100 for x in labels[-1]) == len(target)
        return {'input_ids': torch.tensor(ids, device='cuda'), 'labels': torch.tensor(labels, device='cuda'),
                'attention_mask': torch.tensor(masks, device='cuda')}


    def reference_loss(model, rows):
        model.eval()
        totals, tokens = 0., 0
        with torch.no_grad():
            for start in range(0, len(rows), cfg['microBatch']):
                pack = batch(rows[start:start + cfg['microBatch']])
                count = int((pack['labels'][:, 1:] != -100).sum())
                value = model(**pack).loss
                assert bool(torch.isfinite(value))
                totals += float(value) * count
                tokens += count
        return {'tokenWeightedLoss': totals / tokens, 'targetTokens': tokens, 'scenes': len(rows)}


    if stage == 'prepare':
        assert not (ROOT / 'freeze.json').exists()
        from .audit import iter_json_array
        training_ids, validation_ids = set(), set()
        counts = []
        for name, ids in [('development', training_ids), ('validation', validation_ids)]:
            count = 0
            for row in iter_json_array(ROOT / (name + '-metadata.json')):
                ids.add(row['matchId'])
                count += 1
            counts.append(count)
        assert counts == [cfg['samples'], cfg['validationSamples']]
        assert not training_ids & validation_ids
        train, validation = encode_split('development'), encode_split('validation')
        torch.save({'development': train, 'validation': validation}, ROOT / 'encoded.pt.tmp')
        os.replace(ROOT / 'encoded.pt.tmp', ROOT / 'encoded.pt')
        model = attach(load_base())
        worst = sorted(train, key=lambda r: len(r[0]) + len(r[1]), reverse=True)[:cfg['microBatch']]
        model.train()
        torch.cuda.reset_peak_memory_stats()
        began = time.perf_counter()
        loss = model(**batch(worst)).loss
        assert bool(torch.isfinite(loss))
        loss.backward()
        parameters = [p for p in model.parameters() if p.requires_grad]
        assert parameters and all('lora_' in name for name, p in model.named_parameters() if p.requires_grad)
        norm = torch.nn.utils.clip_grad_norm_(parameters, 1.)
        assert bool(torch.isfinite(norm)) and float(norm) > 0
        torch.cuda.synchronize()
        result = {'scenes': len(train), 'validation': len(validation), 'dtype': str(dtype), 'gpu': torch.cuda.get_device_name(),
            'maximumTokens': max(len(p) + len(t) for p, t in train + validation), 'worstMicrobatchSeconds': time.perf_counter() - began,
            'peakAllocatedBytes': torch.cuda.max_memory_allocated(), 'trainableParameters': sum(p.numel() for p in parameters),
            'microBatches': math.ceil(len(train) / cfg['microBatch']),
            'optimizerSteps': math.ceil(math.ceil(len(train) / cfg['microBatch']) / cfg['accumulation']),
            'optimizerUpdated': False, 'testOpened': False}
        atomic_json(ROOT / 'preflight.json', result)
        frozen_fingerprint = fingerprint()
        frozen = {'runtimePackage': 'coach-ml', 'fingerprint': frozen_fingerprint, 'encodedHash': sha(ROOT / 'encoded.pt'), 'config': cfg,
            'preflight': result, 'environment': frozen_fingerprint['environment']}
        atomic_json(ROOT / 'freeze.json', frozen)
        print(json.dumps(result), flush=True)
        return

    frozen = json.loads((ROOT / 'freeze.json').read_text(encoding='utf-8-sig'))
    assert fingerprint() == frozen['fingerprint'], 'Frozen data/code/base changed'
    assert sha(ROOT / 'encoded.pt') == frozen['encodedHash']
    encoded = torch.load(ROOT / 'encoded.pt', map_location='cpu', weights_only=False)
    train, validation = encoded['development'], encoded['validation']


    def train_epoch():
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
        cursor, step, elapsed_prior = 0, 0, 0.
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
            cursor, step, elapsed_prior = state['cursor'], state['step'], state['elapsedSeconds']
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
            loss_sum = 0.
            for micro in micros:
                loss = model(**batch([train[i] for i in micro])).loss
                assert bool(torch.isfinite(loss)), 'Nonfinite loss'
                (loss / len(micros)).backward()
                loss_sum += float(loss.detach())
            norm = torch.nn.utils.clip_grad_norm_(params, 1.)
            assert bool(torch.isfinite(norm)), 'Nonfinite gradients'
            next_step = step + 1
            factor = next_step / warmup if next_step <= warmup else (total_steps - next_step + 1) / max(1, total_steps - warmup)
            for group in optimizer.param_groups:
                group['lr'] = cfg['learningRate'] * factor
            optimizer.step()
            cursor += len(indexes)
            step = next_step
            entry = {'step': step, 'cursor': cursor, 'rows': indexes, 'meanMicroLoss': loss_sum / len(micros),
                'gradientNorm': float(norm), 'learningRate': optimizer.param_groups[0]['lr'],
                'elapsedSeconds': elapsed_prior + time.perf_counter() - started}
            with log.open('a', encoding='utf-8') as stream:
                stream.write(json.dumps(entry) + '\n')
            atomic_json(ROOT / 'live-status.json', {'status': 'training', 'totalSamples': len(train), 'totalSteps': total_steps, **{k: v for k, v in entry.items() if k != 'rows'}})
            if step % 10 == 0 or cursor == len(order):
                print(json.dumps({k: v for k, v in entry.items() if k != 'rows'}), flush=True)
            if step % cfg['checkpointSteps'] == 0 or cursor == len(order):
                name = f'checkpoint-{step:06}'
                folder = output / (name + '.partial')
                folder.mkdir(exist_ok=False)
                model.save_pretrained(folder, safe_serialization=True)
                torch.save({'optimizer': optimizer.state_dict(), 'cursor': cursor, 'step': step,
                    'elapsedSeconds': entry['elapsedSeconds'], 'pythonRandom': random.getstate(),
                    'torchRandom': torch.get_rng_state(), 'cudaRandom': torch.cuda.get_rng_state_all()}, folder / 'state.pt')
                checkpoint = {'directory': name, 'cursor': cursor, 'step': step, 'freezeHash': sha(ROOT / 'freeze.json')}
                seal_checkpoint(folder, checkpoint)
                folder.rename(output / name)
                verify_checkpoint(output, checkpoint, checkpoint['freezeHash'])
                atomic_json(latest, checkpoint)
                atomic_json(output / 'progress.json', {k: v for k, v in entry.items() if k != 'rows'})
        assert cursor == len(train) and step == total_steps
        adapted_loss = reference_loss(model, validation)
        changed = [float((p.detach().cpu() - initial[n]).abs().sum()) for n, p in model.named_parameters() if n in initial]
        assert sum(changed) > 0
        model.save_pretrained(output / 'adapter-final', safe_serialization=True)
        atomic_json(output / 'effective-visit-order.json', order)
        assert fingerprint() == frozen['fingerprint'], 'Base/data/code mutated'
        result = {'samples': cursor, 'epochs': 1, 'steps': step, 'baseReference': base_loss, 'adaptedReference': adapted_loss,
            'changedTensors': sum(x > 0 for x in changed), 'adapterDeltaL1': sum(changed),
            'peakAllocatedBytes': torch.cuda.max_memory_allocated(), 'elapsedSeconds': elapsed_prior + time.perf_counter() - started,
            'baseWeightsUnchanged': True, 'testUsed': False, 'humanApproved': False, 'tacticalImprovementValidated': False}
        atomic_json(output / 'training-result.json', result)
        print(json.dumps(result), flush=True)
        del model, optimizer, params
        gc.collect()
        torch.cuda.empty_cache()


    def predict():
        assert (output / 'training-result.json').is_file()
        metadata = json.loads((ROOT / 'validation-metadata.json').read_text(encoding='utf-8-sig'))
        assert len(metadata) == len(validation), 'Prediction metadata count mismatch'
        completed = {variant: prediction_prefix(output / f'predictions-{variant}.jsonl', metadata, variant)
                     for variant in ['base', 'adapted']}
        for variant in ['base', 'adapted']:
            file = output / f'predictions-{variant}.jsonl'
            existing_count = completed[variant]
            if existing_count == len(validation):
                continue
            model = load_base()
            if variant == 'adapted':
                model = PeftModel.from_pretrained(model, output / 'adapter-final', is_trainable=False)
            model.eval()
            model.config.use_cache = True
            stop = model.generation_config.eos_token_id
            stops = set(stop if isinstance(stop, list) else [stop]) | {tokenizer.eos_token_id}
            for start in range(0, len(validation), cfg['inferenceBatch']):
                indexes = list(range(start, min(start + cfg['inferenceBatch'], len(validation))))
                if indexes[-1] < existing_count:
                    continue
                prefixes = [validation[i][0] for i in indexes]
                width = max(map(len, prefixes))
                ids = torch.tensor([[tokenizer.eos_token_id] * (width - len(p)) + p for p in prefixes], device='cuda')
                mask = torch.tensor([[0] * (width - len(p)) + [1] * len(p) for p in prefixes], device='cuda')
                began = time.perf_counter()
                with torch.no_grad():
                    generated = model.generate(input_ids=ids, attention_mask=mask, do_sample=False,
                        max_new_tokens=cfg['maxNewTokens'], pad_token_id=tokenizer.eos_token_id)
                for offset, index in enumerate(indexes):
                    if index < existing_count:
                        continue
                    continuation = generated[offset, width:].tolist()
                    end = next((j + 1 for j, token in enumerate(continuation) if token in stops), len(continuation))
                    continuation = continuation[:end]
                    row = {'row': index, 'matchId': metadata[index]['matchId'], 'episodeId': metadata[index]['episodeId'],
                        'heroId': metadata[index]['heroId'], 'exampleId': metadata[index].get('exampleId'),
                        'competencyId': metadata[index].get('competencyId'),
                        'variant': variant, 'text': tokenizer.decode(continuation, skip_special_tokens=True),
                        'newTokens': len(continuation), 'hitTokenLimit': len(continuation) >= cfg['maxNewTokens'] and continuation[-1] not in stops,
                        'batchSeconds': time.perf_counter() - began, 'inferenceBatch': cfg['inferenceBatch']}
                    with file.open('a', encoding='utf-8') as stream:
                        stream.write(json.dumps(row, ensure_ascii=False) + '\n')
                print(json.dumps({'predictionVariant': variant, 'completed': indexes[-1] + 1}), flush=True)
            del model, generated, ids, mask
            gc.collect()
            torch.cuda.empty_cache()


    if stage == 'run':
        atomic_json(ROOT / 'live-status.json', {'status': 'loading-training', 'optimizerUpdated': False})
        train_epoch()
        atomic_json(ROOT / 'live-status.json', {'status': 'evaluating', 'trainingComplete': True})
        predict()
        atomic_json(ROOT / 'live-status.json', {'status': 'training-and-generation-complete', 'trainingComplete': True, 'promoted': False})
    else:
        raise ValueError(stage)
