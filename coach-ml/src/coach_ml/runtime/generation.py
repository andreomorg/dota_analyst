"""Extracted phase; tensor dependencies are supplied by the composition runtime."""
from ..experiments.control import pause_requested, TrainingPaused

def predict(output, json, ROOT, validation, load_base, PeftModel, tokenizer, cfg, torch, time, gc, prediction_prefix):
    assert (output / 'training-result.json').is_file()
    metadata = json.loads((ROOT / 'validation-metadata.json').read_text(encoding='utf-8-sig'))
    assert len(metadata) == len(validation), 'Prediction metadata count mismatch'
    completed = {variant: prediction_prefix(output / f'predictions-{variant}.jsonl', metadata, variant) for variant in ['base', 'adapted']}
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
            if pause_requested():
                del model
                if 'generated' in locals():
                    del generated, ids, mask
                gc.collect()
                torch.cuda.empty_cache()
                raise TrainingPaused('Paused at a prediction batch boundary')
            indexes = list(range(start, min(start + cfg['inferenceBatch'], len(validation))))
            if indexes[-1] < existing_count:
                continue
            prefixes = [validation[i][0] for i in indexes]
            width = max(map(len, prefixes))
            ids = torch.tensor([[tokenizer.eos_token_id] * (width - len(p)) + p for p in prefixes], device='cuda')
            mask = torch.tensor([[0] * (width - len(p)) + [1] * len(p) for p in prefixes], device='cuda')
            began = time.perf_counter()
            with torch.no_grad():
                generated = model.generate(input_ids=ids, attention_mask=mask, do_sample=False, max_new_tokens=cfg['maxNewTokens'], pad_token_id=tokenizer.eos_token_id)
            for offset, index in enumerate(indexes):
                if index < existing_count:
                    continue
                continuation = generated[offset, width:].tolist()
                end = next((j + 1 for j, token in enumerate(continuation) if token in stops), len(continuation))
                continuation = continuation[:end]
                row = {'row': index, 'matchId': metadata[index]['matchId'], 'episodeId': metadata[index]['episodeId'], 'heroId': metadata[index]['heroId'], 'exampleId': metadata[index].get('exampleId'), 'competencyId': metadata[index].get('competencyId'), 'variant': variant, 'text': tokenizer.decode(continuation, skip_special_tokens=True), 'newTokens': len(continuation), 'hitTokenLimit': len(continuation) >= cfg['maxNewTokens'] and continuation[-1] not in stops, 'batchSeconds': time.perf_counter() - began, 'inferenceBatch': cfg['inferenceBatch']}
                with file.open('a', encoding='utf-8') as stream:
                    stream.write(json.dumps(row, ensure_ascii=False) + '\n')
            print(json.dumps({'predictionVariant': variant, 'completed': indexes[-1] + 1}), flush=True)
        del model, generated, ids, mask
        gc.collect()
        torch.cuda.empty_cache()
