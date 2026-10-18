"""Executable package boundaries and use-case injection, without model weights."""
import ast
import copy
from pathlib import Path
import tempfile
import unittest

from coach_ml.core.validation import AuditError
from coach_ml.environment import runtime_code_hashes
from coach_ml.reviews.application import ReviewApplication
from coach_ml.reviews.contracts import Settings


class ArchitectureTests(unittest.TestCase):
    def test_core_and_applications_do_not_import_runtime_or_transport_adapters(self):
        root = Path(__file__).parents[1] / 'src/coach_ml'
        restricted = [(path, {'audit', 'service', 'reviews', 'experiments', 'lora', 'torch', 'peft', 'transformers'})
                      for path in (root / 'core').glob('*.py')]
        restricted += [(root / 'reviews/application.py', {'engines', 'upstream', 'service', 'torch', 'peft', 'transformers'}),
                       (root / 'experiments/application.py', {'backend', 'lora', 'torch', 'peft', 'transformers'})]
        for path, forbidden in restricted:
            for node in ast.walk(ast.parse(path.read_text(encoding='utf-8'))):
                modules = ([node.module or ''] if isinstance(node, ast.ImportFrom) else
                           [item.name for item in node.names] if isinstance(node, ast.Import) else [])
                for module in modules:
                    self.assertFalse(set(module.split('.')) & forbidden, f'{path.name} imports {module}')

    def test_runtime_identity_includes_subpackages_and_detects_changed_engine(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'reviews').mkdir()
            (root / 'runtime.py').write_text('version = 1')
            engine = root / 'reviews/engine.py'
            engine.write_text('policy = 1')
            before = runtime_code_hashes(root)
            self.assertEqual(set(before), {'runtime.py', 'reviews/engine.py'})
            engine.write_text('policy = 2')
            after = runtime_code_hashes(root)
            self.assertNotEqual(before['reviews/engine.py'], after['reviews/engine.py'])
            self.assertEqual(before['runtime.py'], after['runtime.py'])

    def test_application_validates_engine_references_without_network(self):
        context = {'schemaVersion': 1, 'heroId': 1, 'role': {'position': 1}, 'scenes': []}
        class Engine:
            def review(self, value):
                return {'schemaVersion': 1, 'engine': 'fake', 'humanReviewed': False,
                        'report': {'assessments': []}}
        app = ReviewApplication(Settings('architecture-test-key'), Engine(), lambda *_a, **_kw: context)
        result = app.review(copy.deepcopy(context))
        self.assertEqual(result['engine'], 'fake')
        class InvalidEngine:
            def review(self, value):
                return {'schemaVersion': 1, 'engine': 'fake', 'humanReviewed': True,
                        'report': {'assessments': []}}
        app.engine = InvalidEngine()
        with self.assertRaises(AuditError):
            app.review(context)

    def test_generic_validation_imports_preserve_exception_identity(self):
        from coach_ml.audit import AuditError as compatibility_error
        self.assertIs(compatibility_error, AuditError)


if __name__ == '__main__':
    unittest.main()
