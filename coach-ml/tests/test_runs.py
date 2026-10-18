import tempfile
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import unittest
import json
import os
from unittest.mock import patch

from coach_ml.core.validation import AuditError
from coach_ml.experiments.runs import RunStore
from coach_ml.experiments.application import execute
from coach_ml.experiments.control import TrainingPaused
from coach_ml.checkpoints import seal_checkpoint
from coach_ml.hashing import file_hash


class RunStoreTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.path = Path(self.folder.name) / 'runs.sqlite3'
        self.store = RunStore(self.path)

    def test_resource_is_global_and_never_stolen_after_restart(self):
        token = self.store.start('first', self.folder.name, 'freeze')
        restarted = RunStore(self.path)
        with self.assertRaisesRegex(AuditError, 'already owned'):
            restarted.start('second', self.folder.name, 'other')
        restarted.request_pause('first')
        with self.assertRaises(AuditError):
            restarted.transition('first', token, 'completed')
        with self.assertRaises(AuditError):
            restarted.transition('first', token, 'paused')
        restarted.transition('first', token, 'paused', checkpoint={'verified': True})
        self.assertEqual(self.store.get('first')['state'], 'paused')
        new_token = restarted.start('first', self.folder.name, 'freeze')
        with self.assertRaisesRegex(AuditError, 'Stale'):
            restarted.transition('first', token, 'failed')
        restarted.transition('first', new_token, 'completed')
        with self.assertRaises(AuditError):
            restarted.start('first', self.folder.name, 'freeze')

    def test_two_concurrent_claims_have_only_one_owner(self):
        def claim(name):
            try:
                return self.store.start(name, self.folder.name, 'freeze')
            except AuditError:
                return None
        with ThreadPoolExecutor(2) as workers:
            results = list(workers.map(claim, ['one', 'two']))
        self.assertEqual(sum(result is not None for result in results), 1)

    def test_resume_rejects_changed_identity(self):
        token = self.store.start('one', self.folder.name, 'freeze')
        self.store.transition('one', token, 'failed')
        with self.assertRaisesRegex(AuditError, 'identity changed'):
            self.store.start('one', self.folder.name, 'modified')

    def test_preparation_cannot_claim_an_optimizer_checkpoint_pause(self):
        token = self.store.start('prepare', self.folder.name, 'config', initial_state='preparing')
        with self.assertRaises(AuditError):
            self.store.request_pause('prepare')
        self.store.transition('prepare', token, 'prepared')
        self.assertEqual(self.store.get('prepare')['state'], 'prepared')

    def test_application_acknowledges_only_a_verified_pause(self):
        root = Path(self.folder.name) / 'experiment'
        root.mkdir()
        (root / 'freeze.json').write_text(json.dumps({'runtimePackage': 'coach-ml'}))
        def backend(*_args):
            store = RunStore(os.environ['COACH_ML_RUN_STORE'])
            store.request_pause(os.environ['COACH_ML_RUN_ID'])
            output = root / 'output'
            folder = output / 'checkpoint-000001'
            folder.mkdir(parents=True)
            for name in ('adapter_config.json', 'adapter_model.safetensors', 'state.pt'):
                (folder / name).write_bytes(b'trusted-local-synthetic-artifact')
            pointer = {'directory': folder.name, 'step': 1, 'cursor': 1,
                       'freezeHash': file_hash(root / 'freeze.json')}
            seal_checkpoint(folder, pointer)
            (output / 'latest-checkpoint.json').write_text(json.dumps(pointer))
            raise TrainingPaused()
        with patch('coach_ml.experiments.application.inspect_experiment', return_value={}):
            report = execute('run', root, root, root, backend, self.path)
        self.assertEqual(report['status'], 'paused')
        self.assertEqual(self.store.get(report['runId'])['state'], 'paused')
        self.assertFalse((root / '.coach-ml.lock').exists())
        self.assertNotIn('COACH_ML_ATTEMPT', os.environ)


if __name__ == '__main__':
    unittest.main()
