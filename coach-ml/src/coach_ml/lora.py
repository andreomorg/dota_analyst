"""Versioned LoRA runtime for new offline experiments; legacy runs retain their frozen code."""

def run(stage, experiment, model_cache, runtime=None):
    from .runtime.generation import predict
    from .runtime.optimization import train_epoch
    from .runtime.preparation import prepare
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
    from .environment import runtime_environment, runtime_code_hashes
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
    tokenizer = (AutoTokenizer.from_pretrained(snapshot, local_files_only=True, trust_remote_code=False)
                 if runtime is None else runtime.tokenizer(snapshot, cfg))
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
        names = ['config.json', 'instructions.txt', 'development.jsonl', 'validation.jsonl', 'development-metadata.json', 'validation-metadata.json', 'experimental-authorization.json']
        weights = sorted(snapshot.glob('*.safetensors'))
        assert weights
        result = {'files': {n: sha(ROOT / n) for n in names}, 'baseWeights': {p.name: sha(p) for p in weights}, 'revision': cfg['revision'], 'runtimeCode': runtime_code_hashes(), 'environment': runtime_environment()}
        if runtime is not None:
            result['runtime'] = runtime.identity(snapshot, cfg)
        return result

    def encode_split(name):
        encoded = []
        with (ROOT / (name + '.jsonl')).open(encoding='utf-8-sig') as stream:
            for index, line in enumerate(stream):
                messages = json.loads(line)['messages']
                if runtime is None:
                    prompt = tokenizer.apply_chat_template(messages[:2], tokenize=False, add_generation_prompt=True, enable_thinking=False)
                    prefix = tokenizer(prompt, add_special_tokens=False)['input_ids']
                    target = tokenizer(messages[2]['content'] + tokenizer.eos_token, add_special_tokens=False)['input_ids']
                else:
                    prefix, target = runtime.encode(messages, tokenizer, cfg)
                assert prefix and target and (len(prefix) + len(target) <= cfg['maxTrainingTokens']), f'Truncation forbidden: {name} row {index} tokens {len(prefix) + len(target)}'
                encoded.append((prefix, target))
                if (index + 1) % 5000 == 0:
                    atomic_json(ROOT / 'live-status.json', {'status': 'encoding', 'split': name, 'encoded': index + 1, 'optimizerUpdated': False})
                    print(json.dumps({'encodingSplit': name, 'encoded': index + 1}), flush=True)
        return encoded

    def load_base():
        if runtime is not None:
            return runtime.load_base(snapshot, cfg)
        model = AutoModelForCausalLM.from_pretrained(snapshot, local_files_only=True, trust_remote_code=False, dtype=dtype, attn_implementation='sdpa').to('cuda')
        model.config.use_cache = False
        return model

    def attach(model):
        if runtime is not None:
            return runtime.attach(model, cfg)
        model = get_peft_model(model, LoraConfig(r=cfg['loraRank'], lora_alpha=cfg['loraAlpha'], lora_dropout=0, target_modules=cfg['targetModules'], bias='none', task_type='CAUSAL_LM'))
        if cfg['gradientCheckpointing']:
            model.gradient_checkpointing_enable(gradient_checkpointing_kwargs={'use_reentrant': False})
            model.enable_input_require_grads()
        return model

    def batch(rows):
        torch.cuda.empty_cache()
        width = max((len(p) + len(t) for p, t in rows))
        ids, labels, masks = ([], [], [])
        for prefix, target in rows:
            pad = width - len(prefix) - len(target)
            ids.append(prefix + target + [tokenizer.eos_token_id] * pad)
            labels.append([-100] * len(prefix) + target + [-100] * pad)
            masks.append([1] * (len(prefix) + len(target)) + [0] * pad)
            assert labels[-1][:len(prefix)] == [-100] * len(prefix)
            assert sum((x != -100 for x in labels[-1])) == len(target)
        return {'input_ids': torch.tensor(ids, device='cuda'), 'labels': torch.tensor(labels, device='cuda'), 'attention_mask': torch.tensor(masks, device='cuda')}

    def reference_loss(model, rows):
        model.eval()
        totals, tokens = (0.0, 0)
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
        prepare(ROOT, cfg, encode_split, torch, os, attach, load_base, time, batch, dtype, math, atomic_json, fingerprint, sha, json)
        return
    frozen = json.loads((ROOT / 'freeze.json').read_text(encoding='utf-8-sig'))
    assert fingerprint() == frozen['fingerprint'], 'Frozen data/code/base changed'
    assert sha(ROOT / 'encoded.pt') == frozen['encodedHash']
    assert sha(ROOT / 'model-capability.json') == frozen['capabilityHash'], 'Model capability changed'
    encoded = torch.load(ROOT / 'encoded.pt', map_location='cpu', weights_only=False)
    train, validation = (encoded['development'], encoded['validation'])
    if stage == 'run':
        atomic_json(ROOT / 'live-status.json', {'status': 'loading-training', 'optimizerUpdated': False})
        train_epoch(output, time, train, random, cfg, load_base, reference_loss, validation, attach, torch, json, verify_checkpoint, sha, ROOT, frozen, math, batch, atomic_json, seal_checkpoint, fingerprint, gc)
        atomic_json(ROOT / 'live-status.json', {'status': 'evaluating', 'trainingComplete': True})
        from .experiments.control import phase
        phase('generating')
        predict(output, json, ROOT, validation, load_base, PeftModel, tokenizer, cfg, torch, time, gc, prediction_prefix)
        atomic_json(ROOT / 'live-status.json', {'status': 'training-and-generation-complete', 'trainingComplete': True, 'promoted': False})
    else:
        raise ValueError(stage)
