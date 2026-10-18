"""Checkpoint serialization, sealing and publication for trusted local state."""
def save_training_checkpoint(step, output, model, torch, optimizer, cursor, entry, random, sha, ROOT, seal_checkpoint, verify_checkpoint, atomic_json, latest):
    name = f'checkpoint-{step:06}'
    folder = output / (name + '.partial')
    folder.mkdir(exist_ok=False)
    model.save_pretrained(folder, safe_serialization=True)
    torch.save({'optimizer': optimizer.state_dict(), 'cursor': cursor, 'step': step, 'elapsedSeconds': entry['elapsedSeconds'], 'pythonRandom': random.getstate(), 'torchRandom': torch.get_rng_state(), 'cudaRandom': torch.cuda.get_rng_state_all()}, folder / 'state.pt')
    checkpoint = {'directory': name, 'cursor': cursor, 'step': step, 'freezeHash': sha(ROOT / 'freeze.json')}
    seal_checkpoint(folder, checkpoint)
    folder.rename(output / name)
    verify_checkpoint(output, checkpoint, checkpoint['freezeHash'])
    atomic_json(latest, checkpoint)
    atomic_json(output / 'progress.json', {k: v for k, v in entry.items() if k != 'rows'})
