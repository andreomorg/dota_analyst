"""Optional CPU tensor/tokenizer checks; run separately from the tensor-free CLI tests."""
import json
import os
from pathlib import Path
import tempfile
import unittest

from coach_ml.core.validation import AuditError


@unittest.skipUnless(os.environ.get('COACH_ML_QWEN35_SNAPSHOT'), 'requires the explicit local Qwen3.5 snapshot')
class Qwen35NativeTests(unittest.TestCase):
    def test_official_checkpoint_has_complete_text_weights_without_loading_payload(self):
        import torch
        from coach_ml.runtime_qwen35.model import verify_snapshot, validate_text_layout
        snapshot = Path(os.environ['COACH_ML_QWEN35_SNAPSHOT'])
        verify_snapshot(snapshot)
        layout = validate_text_layout(snapshot)
        self.assertEqual(layout['textTensors'], 427)
        self.assertEqual(layout['ignoredTensors'], 312)
        self.assertEqual(layout['targetCounts']['in_proj_qkv'], 24)
        self.assertEqual(layout['targetCounts']['q_proj'], 8)
        self.assertFalse(torch.cuda.is_initialized())

    def test_actual_tokenizer_encodes_one_json_answer_and_one_message_terminator(self):
        import torch
        from transformers import AutoTokenizer
        from coach_ml.runtime_qwen35.encoding import encode_example
        tokenizer = AutoTokenizer.from_pretrained(os.environ['COACH_ML_QWEN35_SNAPSHOT'],
                                                 local_files_only=True, trust_remote_code=False)
        answer = json.dumps({'schemaVersion': 1, 'testOnly': True})
        messages = [{'role': 'system', 'content': 'Responda somente com o JSON solicitado.'},
                    {'role': 'user', 'content': 'Exemplo sintético de teste.'},
                    {'role': 'assistant', 'content': answer}]
        prefix, target = encode_example(messages, tokenizer, {'maxTrainingTokens': 3584})
        self.assertTrue(prefix)
        self.assertEqual(target.count(tokenizer.eos_token_id), 1)
        self.assertEqual(tokenizer.decode(target, skip_special_tokens=True), answer)
        self.assertFalse(torch.cuda.is_initialized())

    def test_real_tiny_text_checkpoint_rejects_wrong_tensor_shape(self):
        import torch
        from safetensors.torch import save_file
        from transformers import Qwen3_5ForCausalLM, Qwen3_5TextConfig
        from coach_ml.runtime_qwen35.model import validate_text_layout
        config = Qwen3_5TextConfig(vocab_size=32, hidden_size=16, intermediate_size=32, num_hidden_layers=2,
                                  num_attention_heads=2, num_key_value_heads=1, head_dim=8,
                                  linear_num_value_heads=2, linear_num_key_heads=2,
                                  linear_key_head_dim=8, linear_value_head_dim=8,
                                  layer_types=['linear_attention', 'full_attention'], tie_word_embeddings=False)
        model = Qwen3_5ForCausalLM(config)
        tensors = {'model.language_model.' + key.removeprefix('model.') if key.startswith('model.') else key: value
                   for key, value in model.state_dict().items()}
        tensors['model.visual.test'] = torch.zeros(1)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'config.json').write_text(json.dumps({'model_type': 'qwen3_5', 'text_config': config.to_dict()}))
            (root / 'model.safetensors.index.json').write_text(json.dumps({
                'weight_map': {name: 'tiny.safetensors' for name in tensors}}))
            save_file(tensors, root / 'tiny.safetensors')
            layout = validate_text_layout(root)
            self.assertTrue(all(count == 1 for count in layout['targetCounts'].values()))
            tensors['model.language_model.norm.weight'] = torch.zeros(17)
            save_file(tensors, root / 'tiny.safetensors')
            with self.assertRaisesRegex(AuditError, 'shape mismatch'):
                validate_text_layout(root)
        self.assertFalse(torch.cuda.is_initialized())
