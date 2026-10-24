import gc
import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from coach_ml.experiments.control import TrainingPaused
from coach_ml.predictions import prediction_prefix
from coach_ml.runtime.generation import predict, predict_job
from coach_ml.runtime.ports import GenerationJob, GenerationResources, ModelRuntime
from coach_ml.runtime_qwen35.training import Qwen35Runtime


class Grid:
    def __init__(self, rows):
        self.rows = rows

    def __getitem__(self, key):
        row, column = key
        return SimpleNamespace(tolist=lambda: self.rows[row][column])


class NoGrad:
    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False


class GenerationCompositionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.output = self.root / 'output'
        self.output.mkdir()
        (self.output / 'training-result.json').write_text('{}')
        metadata = [{'matchId': '1', 'episodeId': 'scene', 'heroId': 1, 'exampleId': 'example'}]
        (self.root / 'validation-metadata.json').write_text(json.dumps(metadata))
        self.tokenizer = SimpleNamespace(eos_token_id=9, decode=lambda tokens, **kwargs: str(tokens))
        self.job = GenerationJob(self.root, self.output, {'inferenceBatch': 1, 'maxNewTokens': 8},
                                 [([1, 2], [3, 9])], self.tokenizer)
        self.loaded = []
        def load():
            self.loaded.append(True)
            return SimpleNamespace(config=SimpleNamespace(), generation_config=SimpleNamespace(eos_token_id=9),
                                   eval=lambda: None, generate=lambda **kwargs: Grid([[1, 2, 3, 9, 8]]))
        self.resources = GenerationResources(
            torch=SimpleNamespace(tensor=lambda rows, **kwargs: Grid(rows), no_grad=NoGrad,
                                  cuda=SimpleNamespace(empty_cache=lambda: None)),
            json=json, time=SimpleNamespace(perf_counter=lambda: 0), gc=gc, load_base=load,
            adapter_loader=SimpleNamespace(from_pretrained=lambda model, *args, **kwargs: model),
            prediction_prefix=prediction_prefix)

    def test_composed_generation_keeps_stop_boundary_identity_and_completed_prefix(self):
        with patch('sys.stdout'):
            predict_job(self.job, self.resources)
            predict_job(self.job, self.resources)
        self.assertEqual(len(self.loaded), 2)
        for variant in ('base', 'adapted'):
            rows = (self.output / f'predictions-{variant}.jsonl').read_text().splitlines()
            self.assertEqual(len(rows), 1)
            row = json.loads(rows[0])
            self.assertEqual(row['text'], '[3, 9]')
            self.assertEqual(row['newTokens'], 2)
            self.assertFalse(row['hitTokenLimit'])
            self.assertEqual((row['row'], row['exampleId'], row['variant']), (0, 'example', variant))

    def test_pause_and_legacy_adapter_resume_without_repeating_completed_base(self):
        with patch('sys.stdout'), patch('coach_ml.runtime.generation.pause_requested', side_effect=[False, True]):
            with self.assertRaises(TrainingPaused): predict_job(self.job, self.resources)
        base = (self.output / 'predictions-base.jsonl').read_bytes()
        self.assertFalse((self.output / 'predictions-adapted.jsonl').exists())
        r, j = self.resources, self.job
        with patch('sys.stdout'):
            predict(j.output, r.json, j.root, j.validation, r.load_base, r.adapter_loader, j.tokenizer,
                    j.cfg, r.torch, r.time, r.gc, r.prediction_prefix)
        self.assertEqual((self.output / 'predictions-base.jsonl').read_bytes(), base)
        self.assertEqual(len((self.output / 'predictions-adapted.jsonl').read_text().splitlines()), 1)
        self.assertEqual(len(self.loaded), 3)

    def test_qwen_runtime_satisfies_explicit_port_without_initializing_tensors(self):
        self.assertIsInstance(Qwen35Runtime(None, None, {}), ModelRuntime)
        self.assertNotIsInstance(object(), ModelRuntime)
