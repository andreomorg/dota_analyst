import json
import importlib.util
import os
import subprocess
import sys
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from coach_ml.core.validation import AuditError
from coach_ml.runtime_qwen35.config import VERSIONS
from coach_ml.runtime_qwen35.encoding import encode_example
from coach_ml.runtime_qwen35.training import Qwen35Runtime, environment_identity
from test_qwen35_runtime import config


class Tokenizer:
    eos_token, eos_token_id = '<end>', 9
    def apply_chat_template(self, messages, **kwargs):
        assert kwargs == dict(tokenize=False, add_generation_prompt=True, enable_thinking=False)
        assert [m['role'] for m in messages] == ['system', 'user']
        return 'prompt'
    def __call__(self, text, **kwargs):
        assert kwargs == dict(add_special_tokens=False)
        return {'input_ids': [1, 2] if text == 'prompt' else [3, 9]}


class Qwen35TrainingTests(unittest.TestCase):
    def test_gpu_qualification_requires_explicit_opt_in_before_reading_data(self):
        script = Path(__file__).parent / 'gpu/verify_qwen35.py'
        environment = {**os.environ}
        environment.pop('COACH_ML_QWEN35_GPU_TEST', None)
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'qualification'
            result = subprocess.run([sys.executable, '-B', str(script), '--snapshot', 'missing',
                                     '--config', 'missing', '--output', str(output)],
                                    env=environment, capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('Explicit night-time GPU test opt-in required', result.stderr)
            self.assertFalse(output.exists())

    def test_message_boundaries_and_no_truncation(self):
        messages = [{'role': role, 'content': 'value'} for role in ('system', 'user', 'assistant')]
        self.assertEqual(encode_example(messages, Tokenizer(), config()), ([1, 2], [3, 9]))
        with self.assertRaises(AuditError):
            encode_example(messages, Tokenizer(), config() | {'maxTrainingTokens': 3})
        with self.assertRaises(AuditError):
            encode_example(messages[:2], Tokenizer(), config())

    def test_image_and_kernel_version_changes_are_rejected(self):
        with patch.dict(os.environ, {'COACH_ML_RUNTIME_IMAGE': 'sha256:' + 'a' * 64}), \
             patch('coach_ml.runtime_qwen35.training.metadata.version', side_effect=lambda name: VERSIONS[name]):
            identity = environment_identity()
            self.assertEqual(identity['imageId'], 'sha256:' + 'a' * 64)
            with patch.dict(os.environ, {'COACH_ML_RUNTIME_IMAGE': 'mutable-tag'}):
                with self.assertRaises(AuditError): environment_identity()
            with patch('coach_ml.runtime_qwen35.training.metadata.version', return_value='unqualified'):
                with self.assertRaises(AuditError): environment_identity()

    def test_fingerprint_covers_tokenizer_and_runtime_image(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for name in ('tokenizer.json', 'tokenizer_config.json', 'chat_template.jinja'):
                (root / name).write_text('original')
            hooks = Qwen35Runtime(None, None, {'layoutSha256': 'fixed'})
            with patch('coach_ml.runtime_qwen35.training.environment_identity', return_value={'imageId': 'fixed'}):
                before = hooks.identity(root, config())
                (root / 'chat_template.jinja').write_text('changed')
                self.assertNotEqual(before, hooks.identity(root, config()))
                self.assertEqual(before['id'], 'qwen35-text-bf16-v1')

    def test_adapter_cannot_train_base_or_visual_weights(self):
        parameter = SimpleNamespace(requires_grad=True)
        model = SimpleNamespace(named_parameters=lambda: [('base_model.model.layers.0.q_proj.lora_A.weight', parameter)])
        framework = SimpleNamespace(get_peft_model=lambda *args, **kwargs: model)
        hooks = Qwen35Runtime(framework, None, {})
        self.assertIs(hooks.attach(model, config()), model)
        model.named_parameters = lambda: [('base_model.model.embed_tokens.weight', parameter)]
        with self.assertRaises(AuditError): hooks.attach(model, config())

    @unittest.skipUnless(importlib.util.find_spec('torch'), 'requires tensor runtime CPU image')
    def test_real_phase_composition_injects_ports_and_preserves_label_masks(self):
        if os.environ.get('COACH_ML_TENSOR_TEST_CHILD') != '1':
            # Keep the main test process tensor-free for the existing CLI/import isolation checks.
            result = subprocess.run([sys.executable, '-B', '-m', 'unittest',
                __name__ + '.Qwen35TrainingTests.test_real_phase_composition_injects_ports_and_preserves_label_masks', '-q'],
                env={**os.environ, 'COACH_ML_TENSOR_TEST_CHILD': '1'}, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            return
        import torch
        from coach_ml.lora import run
        from coach_ml.runtime_qwen35.config import REVISION
        messages = [{'role': role, 'content': 'value'} for role in ('system', 'user', 'assistant')]
        hooks = SimpleNamespace(tokenizer=lambda *args: Tokenizer(), encode=encode_example,
                                load_base=lambda *args: 'base', attach=lambda *args: 'adapter',
                                identity=lambda *args: {'id': 'test-runtime'})
        original_tensor = torch.tensor
        def cpu_tensor(*args, **kwargs):
            kwargs['device'] = 'cpu'
            return original_tensor(*args, **kwargs)
        def prepare(root, cfg, encode, tensor, os, attach, load, time, batch, dtype, math,
                    atomic_json, fingerprint, sha, json):
            self.assertEqual(encode('development'), [([1, 2], [3, 9])])
            self.assertEqual(load(), 'base')
            self.assertEqual(attach('base'), 'adapter')
            value = batch([([1, 2], [3, 9]), ([1], [9])])
            self.assertEqual(value['labels'].tolist(), [[-100, -100, 3, 9], [-100, 9, -100, -100]])
            self.assertEqual(value['attention_mask'].tolist(), [[1, 1, 1, 1], [1, 1, 0, 0]])
            self.assertEqual(fingerprint()['runtime'], {'id': 'test-runtime'})
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            snapshot = root / 'cache/hub/models--Qwen--Qwen3.5-4B/snapshots' / REVISION
            snapshot.mkdir(parents=True)
            (snapshot / 'weights.safetensors').write_bytes(b'fake-test-weight-identity')
            (root / 'config.json').write_text(json.dumps(config()))
            (root / 'development.jsonl').write_text(json.dumps({'messages': messages}) + '\n')
            for name in ('instructions.txt', 'validation.jsonl', 'development-metadata.json',
                         'validation-metadata.json', 'experimental-authorization.json'):
                (root / name).write_text('test')
            with patch('coach_ml.runtime.preparation.prepare', side_effect=prepare) as called, \
                 patch.object(torch.cuda, 'is_available', return_value=True), \
                 patch.object(torch.cuda, 'is_bf16_supported', return_value=True), \
                 patch.object(torch.cuda, 'manual_seed_all'), patch.object(torch.cuda, 'empty_cache'), \
                 patch.object(torch, 'tensor', side_effect=cpu_tensor):
                run('prepare', root, root / 'cache', runtime=hooks)
            called.assert_called_once()
            self.assertFalse(torch.cuda.is_initialized())
