import json
from pathlib import Path
import struct
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from coach_ml.core.validation import AuditError
from coach_ml.experiments.preparation import load_config
from coach_ml.runtime_qwen35.config import TARGETS
from coach_ml.runtime_qwen35.model import checkpoint_shapes, load_text_model, verify_snapshot
from coach_ml.hashing import file_hash


def config():
    return json.loads((Path(__file__).parents[1] / 'configs/qwen35-4b.rtx3080.example.json').read_text(encoding='utf-8-sig'))


class Qwen35RuntimeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def load(self, value):
        (self.root / 'config.json').write_text(json.dumps(value))
        return load_config(self.root)

    def test_valid_config_preserves_dictionary_and_legacy_default(self):
        value = config()
        self.assertEqual(self.load(value), value)
        legacy = json.loads((Path(__file__).parents[1] / 'configs/lora.example.json').read_text())
        self.assertEqual(self.load(legacy), legacy)

    def test_runtime_cannot_be_implicitly_selected_or_change_recipe(self):
        for changes in ({'runtime': None}, {'runtime': 'unknown'}, {'model': 'Qwen/Qwen3-0.6B'},
                        {'revision': 'a' * 40}, {'precision': 'fp16'}, {'quantization': '4bit'},
                        {'textOnly': False}, {'microBatch': 2}, {'gradientCheckpointing': False},
                        {'loraRank': 32}, {'targetModules': ['q_proj', 'v_proj']}, {'offloadEmbedding': None}):
            with self.subTest(changes=changes), self.assertRaises(AuditError):
                self.load(config() | changes)

    def test_backend_dispatch_is_lazy_and_explicit(self):
        from coach_ml.experiments.backend import run_lora
        self.load(config())
        with patch('coach_ml.runtime_qwen35.run') as selected, patch('coach_ml.lora.run') as legacy:
            run_lora('prepare', self.root, self.root)
            selected.assert_called_once_with('prepare', self.root, self.root)
            legacy.assert_not_called()
        value = json.loads((Path(__file__).parents[1] / 'configs/lora.example.json').read_text())
        self.load(value)
        with patch('coach_ml.runtime_qwen35.run') as selected, patch('coach_ml.lora.run') as legacy:
            run_lora('prepare', self.root, self.root)
            legacy.assert_called_once()
            selected.assert_not_called()

    def test_local_snapshot_hashes_are_enforced(self):
        path = self.root / 'config.json'
        path.write_text('{}')
        expected = {'config.json': {'bytes': 2, 'sha256': file_hash(path)}}
        verify_snapshot(self.root, expected=expected)
        path.write_text('[]')
        with self.assertRaises(AuditError): verify_snapshot(self.root, expected=expected)
        with self.assertRaises(AuditError): verify_snapshot(self.root, expected={'../bad': {}})

    def test_shard_mapping_does_not_load_tensor_payload(self):
        header = {'model.language_model.norm.weight': {'dtype': 'BF16', 'shape': [16], 'data_offsets': [0, 32]},
                  'model.visual.test': {'dtype': 'BF16', 'shape': [1], 'data_offsets': [32, 34]}}
        raw = json.dumps(header).encode()
        (self.root / 'weights.safetensors').write_bytes(struct.pack('<Q', len(raw)) + raw + b'\0' * 34)
        index = {'weight_map': {name: 'weights.safetensors' for name in header}}
        (self.root / 'model.safetensors.index.json').write_text(json.dumps(index))
        shapes, ignored = checkpoint_shapes(self.root)
        self.assertEqual(shapes, {'model.norm.weight': [16]})
        self.assertEqual(ignored, ['model.visual.test'])
        index['weight_map']['unknown.weight'] = 'weights.safetensors'
        (self.root / 'model.safetensors.index.json').write_text(json.dumps(index))
        with self.assertRaises(AuditError): checkpoint_shapes(self.root)

    def test_loader_uses_bf16_text_only_and_rejects_multimodal_fallback(self):
        model = SimpleNamespace(config=SimpleNamespace(model_type='qwen3_5_text'), parameters=lambda: [],
                                named_modules=lambda: [(f'model.layers.0.{target}', None) for target in TARGETS])
        framework = SimpleNamespace(from_pretrained=lambda **kwargs: (model, 'tokenizer'))
        with patch.object(framework, 'from_pretrained', wraps=framework.from_pretrained) as load:
            result, _ = load_text_model(self.root, config(), framework, SimpleNamespace(bfloat16='bf16'))
            self.assertIs(result, model)
            kwargs = load.call_args.kwargs
            self.assertEqual(kwargs['max_seq_length'], config()['maxTrainingTokens'] + config()['maxNewTokens'])
            self.assertTrue(kwargs['text_only'])
            self.assertTrue(kwargs['load_in_16bit'])
            self.assertTrue(kwargs['local_files_only'])
            self.assertFalse(kwargs['trust_remote_code'])
            self.assertFalse(kwargs['load_in_4bit'])
            self.assertFalse(kwargs['use_kernels'])
        model.config.model_type = 'qwen3_5'
        with self.assertRaises(AuditError):
            load_text_model(self.root, config(), framework, SimpleNamespace(bfloat16='bf16'))
