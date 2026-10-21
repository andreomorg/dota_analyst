"""Optional local-only GPU inference for comparing the base and existing adapter."""
from pathlib import Path
import hashlib
import time

from ..core.json import canonical, digest
from ..core.validation import require
from .pipeline import SYSTEM


def trim_tokens(tokens, stops):
    for index, token in enumerate(tokens):
        if token in stops:
            return tokens[:index+1], 'stop'
    return tokens, 'length'


def generator(snapshot, adapter, max_tokens, requests=None, batch_size=4):
    # Deliberately lazy: data-only commands and the HTTP service never import tensor dependencies.
    import torch
    from transformers import AutoTokenizer, AutoModelForCausalLM
    require(torch.cuda.is_available(), 'CUDA is required for the comparison backend')
    snapshot = Path(snapshot).resolve()
    require(snapshot.is_dir(), 'Pinned snapshot missing')
    require((snapshot/'config.json').is_file(), 'Missing local model config')
    weights = list(snapshot.glob('*.safetensors'))
    require(bool(weights), 'Missing local model weights')
    identity = {'engine': 'huggingface-local', 'snapshotRevision': snapshot.name,
                'maxNewTokens': max_tokens, 'thinking': False, 'temperature': 0, 'inferenceBatch': batch_size,
                'files': {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                          for p in [snapshot/'config.json', *weights]}}
    tokenizer = AutoTokenizer.from_pretrained(snapshot, local_files_only=True, trust_remote_code=False)
    tokenizer.padding_side = 'left'
    tokenizer.pad_token_id = tokenizer.eos_token_id
    model = AutoModelForCausalLM.from_pretrained(snapshot, local_files_only=True, trust_remote_code=False,
                                                torch_dtype=torch.bfloat16).to('cuda')
    if adapter is not None:
        from peft import PeftModel
        adapter = Path(adapter)
        for name in ('adapter_config.json', 'adapter_model.safetensors'):
            require((adapter/name).is_file(), 'Missing local adapter')
        identity['adapterFiles'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                    for p in (adapter/'adapter_config.json', adapter/'adapter_model.safetensors')}
        model = PeftModel.from_pretrained(model, adapter, is_trainable=False, local_files_only=True)
    model.eval()
    model.config.use_cache = True
    stop = model.generation_config.eos_token_id
    stops = set(stop if isinstance(stop, list) else [stop]) | {tokenizer.eos_token_id}

    requests = requests or []
    indexes = {digest(request): index for index, request in enumerate(requests)}
    cache = {}

    def generate(request):
        key = digest(request)
        if key in cache:
            return cache.pop(key)
        start = indexes.get(key)
        batch = [request] if start is None else requests[start:start+batch_size]
        texts = []
        for item in batch:
            messages = [{'role': 'system', 'content': SYSTEM}, {'role': 'user', 'content': canonical(item)}]
            texts.append(tokenizer.apply_chat_template(messages, tokenize=False, add_generation_prompt=True, enable_thinking=False))
        inputs = tokenizer(texts, return_tensors='pt', padding=True).to('cuda')
        require(inputs['input_ids'].shape[1] + max_tokens <= 8192, 'Comparison token budget exceeded')
        torch.cuda.reset_peak_memory_stats()
        began = time.perf_counter()
        with torch.inference_mode():
            output = model.generate(**inputs, max_new_tokens=max_tokens, do_sample=False,
                                    pad_token_id=tokenizer.eos_token_id)
        continuations = output[:, inputs['input_ids'].shape[1]:].tolist()
        elapsed = time.perf_counter()-began
        for item, continuation, mask in zip(batch, continuations, inputs['attention_mask']):
            tokens, reason = trim_tokens(continuation, stops)
            cache[digest(item)] = {'text': tokenizer.decode(tokens, skip_special_tokens=True), 'tokens': len(tokens),
                                  'stopReason': reason, 'promptTokens': int(mask.sum().item()),
                                  'inferenceBatch': len(batch), 'batchSeconds': elapsed,
                                  'peakAllocatedBytes': torch.cuda.max_memory_allocated()}
        return cache.pop(key)
    return identity, generate
