import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from coach_ml.experiments.control import TrainingPaused
from coach_ml.runtime.optimization import train_epoch
from fixtures.fake_optimizer import environment, resume_modules
from fixtures.optimization_before_split import train_epoch as legacy_train


class MultiEpochTests(unittest.TestCase):
    def run_training(self, root, epochs, pause_step=None, function=train_epoch):
        env = environment(root, epochs)
        count = 0
        def pause():
            nonlocal count
            count += 1
            return count == pause_step
        with patch.dict('sys.modules', resume_modules()), patch('coach_ml.runtime.optimization.pause_requested', pause):
            function(**env)
        return env

    def test_single_epoch_preserves_legacy_order_state_and_result(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for name, function in [('old', legacy_train), ('new', train_epoch)]:
                self.run_training(root / name, 1, function=function)
            for name in ('effective-visit-order.json', 'adapter-final/adapter_model.safetensors', 'latest-checkpoint.json'):
                self.assertEqual((root / 'old/output' / name).read_bytes(), (root / 'new/output' / name).read_bytes())
            a = json.loads((root / 'old/output/training-result.json').read_text())
            b = json.loads((root / 'new/output/training-result.json').read_text())
            a.pop('elapsedSeconds'); b.pop('elapsedSeconds')
            self.assertEqual(a, b)
            env = environment(root / 'new', 1)
            state = env['torch'].load(root / 'new/output/checkpoint-000002/state.pt')
            self.assertNotIn('epoch', state)
            self.assertNotIn('cursorInEpoch', state)

    def test_resume_inside_second_epoch_matches_continuous_final_weights_and_order(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            self.run_training(root / 'full', 3)
            with self.assertRaises(TrainingPaused):
                self.run_training(root / 'resume', 3, pause_step=3)
            pointer = json.loads((root / 'resume/output/latest-checkpoint.json').read_text())
            self.assertEqual((pointer['epoch'], pointer['cursorInEpoch'], pointer['cursor']), (1, 4, 9))
            self.run_training(root / 'resume', 3)
            for name in ('effective-visit-order.json', 'adapter-final/adapter_model.safetensors'):
                self.assertEqual((root / 'full/output' / name).read_bytes(), (root / 'resume/output' / name).read_bytes())
            result = json.loads((root / 'resume/output/training-result.json').read_text())
            self.assertEqual((result['epochs'], result['samples'], result['steps']), (3, 15, 6))
            order = json.loads((root / 'full/output/effective-visit-order.json').read_text())
            for epoch in range(3):
                self.assertEqual(sorted(order[epoch * 5:(epoch + 1) * 5]), list(range(5)))
            self.assertNotEqual(order[:5], order[5:10])
            entries = [json.loads(line) for line in (root / 'full/output/attempt-001.jsonl').read_text().splitlines()]
            self.assertEqual([len(e['rows']) for e in entries], [4, 1, 4, 1, 4, 1])
            self.assertEqual([round(e['learningRate'], 5) for e in entries], [.005, .01, .01, .0075, .005, .0025])

    def test_config_accepts_only_integer_epochs_one_to_four(self):
        from coach_ml.experiments.preparation import load_config
        from coach_ml.core.validation import AuditError
        config = json.loads((Path(__file__).parents[1] / 'configs/lora.example.json').read_text())
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for epochs in (1, 2, 3, 4, 0, 5, True, 1.5):
                (root / 'config.json').write_text(json.dumps({**config, 'epochs': epochs}))
                if type(epochs) is int and 1 <= epochs <= 4:
                    self.assertEqual(load_config(root)['epochs'], epochs)
                else:
                    with self.assertRaises(AuditError): load_config(root)

    def test_epoch_counters_are_sealed_and_single_epoch_pointer_stays_legacy(self):
        from coach_ml.checkpoints import verify_checkpoint
        from coach_ml.core.validation import AuditError
        from coach_ml.hashing import file_hash
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            self.run_training(root / 'multi', 2)
            output = root / 'multi/output'
            pointer = json.loads((output / 'latest-checkpoint.json').read_text())
            for changes in ({'epoch': True}, {'epoch': 5}, {'cursorInEpoch': -1}, {'cursorInEpoch': 1}):
                with self.assertRaises(AuditError):
                    verify_checkpoint(output, pointer | changes, file_hash(root / 'multi/freeze.json'))
            self.run_training(root / 'single', 1)
            pointer = json.loads((root / 'single/output/latest-checkpoint.json').read_text())
            self.assertEqual(set(pointer), {'directory', 'cursor', 'step', 'freezeHash'})
