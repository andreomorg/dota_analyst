import json
from pathlib import Path
import tempfile
import unittest

from coach_ml.audit import AuditError
from coach_ml.checkpoints import FILES, seal_checkpoint, verify_checkpoint


class CheckpointIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.pointer = {'directory': 'checkpoint-000025', 'cursor': 400,
                        'step': 25, 'freezeHash': 'a' * 64}
        self.folder = self.root / self.pointer['directory']
        self.folder.mkdir()
        for name in FILES:
            (self.folder / name).write_bytes(b'synthetic-test-artifact')

    def verify(self):
        return verify_checkpoint(self.root, self.pointer, 'a' * 64)

    def test_sealed_checkpoint_roundtrip(self):
        seal_checkpoint(self.folder, self.pointer)
        self.assertEqual(self.folder, self.verify())

    def test_each_corrupt_artifact_is_rejected(self):
        seal_checkpoint(self.folder, self.pointer)
        for name in FILES:
            with self.subTest(name=name):
                path = self.folder / name
                original = path.read_bytes()
                path.write_bytes(b'x' * len(original))
                with self.assertRaises(AuditError):
                    self.verify()
                path.write_bytes(original)

    def test_legacy_without_manifest_is_rejected_without_mutation(self):
        before = {p.name: p.read_bytes() for p in self.folder.iterdir()}
        with self.assertRaises(AuditError):
            self.verify()
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.folder.iterdir()})

    def test_null_manifest_rejected(self):
        (self.folder / 'checkpoint-manifest.json').write_bytes(b'\x00' * 200)
        with self.assertRaises(AuditError):
            self.verify()

    def test_freeze_and_pointer_mismatch(self):
        seal_checkpoint(self.folder, self.pointer)
        with self.assertRaises(AuditError):
            verify_checkpoint(self.root, self.pointer, 'b' * 64)
        self.pointer['cursor'] += 1
        with self.assertRaises(AuditError):
            self.verify()

    def test_invalid_references_rejected(self):
        for change in ({'directory': '../escape'}, {'step': True}, {'cursor': -1},
                       {'directory': 'checkpoint-000026'}, {'freezeHash': None}):
            with self.subTest(change=change), self.assertRaises(AuditError):
                verify_checkpoint(self.root, self.pointer | change, 'a' * 64)

    def test_partial_files_and_existing_seal_preserved(self):
        state = self.folder / 'state.pt'
        state.write_bytes(b'')
        with self.assertRaises(AuditError):
            seal_checkpoint(self.folder, self.pointer)
        self.assertFalse((self.folder / 'checkpoint-manifest.json').exists())
        state.write_bytes(b'synthetic')
        seal_checkpoint(self.folder, self.pointer)
        before = (self.folder / 'checkpoint-manifest.json').read_bytes()
        with self.assertRaises(FileExistsError):
            seal_checkpoint(self.folder, self.pointer)
        self.assertEqual(before, (self.folder / 'checkpoint-manifest.json').read_bytes())

    def test_symlink_artifact_rejected(self):
        path = self.folder / 'state.pt'
        path.unlink()
        target = self.root / 'external'
        target.write_bytes(b'synthetic')
        try:
            path.symlink_to(target)
        except OSError:
            self.skipTest('Symlinks unavailable')
        with self.assertRaises(AuditError):
            seal_checkpoint(self.folder, self.pointer)

    def test_extra_manifest_file_rejected(self):
        seal_checkpoint(self.folder, self.pointer)
        path = self.folder / 'checkpoint-manifest.json'
        value = json.loads(path.read_text())
        value['files']['extra'] = {'bytes': 1, 'sha256': 'b' * 64}
        path.write_text(json.dumps(value))
        with self.assertRaises(AuditError):
            self.verify()
