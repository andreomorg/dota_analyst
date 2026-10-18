"""Extracted phase; tensor dependencies are supplied by the composition runtime."""
from ..experiments.capabilities import experiment_capability

def prepare(ROOT, cfg, encode_split, torch, os, attach, load_base, time, batch, dtype, math, atomic_json, fingerprint, sha, json):
    assert not (ROOT / 'freeze.json').exists()
    from coach_ml.core.streams import iter_json_array
    training_ids, validation_ids = (set(), set())
    counts = []
    for name, ids in [('development', training_ids), ('validation', validation_ids)]:
        count = 0
        for row in iter_json_array(ROOT / (name + '-metadata.json')):
            ids.add(row['matchId'])
            count += 1
        counts.append(count)
    assert counts == [cfg['samples'], cfg['validationSamples']]
    assert not training_ids & validation_ids
    train, validation = (encode_split('development'), encode_split('validation'))
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
    assert parameters and all(('lora_' in name for name, p in model.named_parameters() if p.requires_grad))
    norm = torch.nn.utils.clip_grad_norm_(parameters, 1.0)
    assert bool(torch.isfinite(norm)) and float(norm) > 0
    torch.cuda.synchronize()
    result = {'scenes': len(train), 'validation': len(validation), 'dtype': str(dtype), 'gpu': torch.cuda.get_device_name(), 'maximumTokens': max((len(p) + len(t) for p, t in train + validation)), 'worstMicrobatchSeconds': time.perf_counter() - began, 'peakAllocatedBytes': torch.cuda.max_memory_allocated(), 'trainableParameters': sum((p.numel() for p in parameters)), 'microBatches': math.ceil(len(train) / cfg['microBatch']), 'optimizerSteps': math.ceil(math.ceil(len(train) / cfg['microBatch']) / cfg['accumulation']), 'optimizerUpdated': False, 'testOpened': False}
    atomic_json(ROOT / 'preflight.json', result)
    capability = experiment_capability(ROOT, cfg)
    atomic_json(ROOT / 'model-capability.json', capability)
    frozen_fingerprint = fingerprint()
    frozen = {'runtimePackage': 'coach-ml', 'fingerprint': frozen_fingerprint, 'encodedHash': sha(ROOT / 'encoded.pt'), 'config': cfg, 'preflight': result, 'environment': frozen_fingerprint['environment'], 'capabilityHash': sha(ROOT / 'model-capability.json')}
    atomic_json(ROOT / 'freeze.json', frozen)
    print(json.dumps(result), flush=True)
    return
