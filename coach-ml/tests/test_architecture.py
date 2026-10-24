import ast
import copy
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from coach_ml.core.validation import AuditError
from coach_ml.environment import runtime_code_hashes
from coach_ml.reviews.application import ReviewApplication
from coach_ml.reviews.contracts import Settings


class RuntimeBoundaryTests(unittest.TestCase):
    @property
    def source(self):
        import coach_ml
        return Path(coach_ml.__file__).parent

    def test_cpu_entrypoints_and_ports_do_not_import_tensor_stack(self):
        code = ('import sys; import coach_ml.cli; import coach_ml.service; '
                'import coach_ml.runtime.ports; import coach_ml.runtime_qwen35.audit; '
                'import coach_ml.tactical.supervision_audit; '
                'assert not ({"torch", "transformers", "unsloth", "peft"} & set(sys.modules))')
        subprocess.run([sys.executable, '-B', '-c', code], check=True, capture_output=True)

    def test_policy_and_contract_modules_have_no_transport_dependency(self):
        for name in ('policy_v2', 'policy_v3', 'target_rendering', 'contracts', 'signal_wording'):
            path = self.source / 'tactical' / (name + '.py')
            tree = ast.parse(path.read_text(encoding='utf-8-sig'))
            imports = []
            for node in ast.walk(tree):
                if isinstance(node, ast.Import):
                    imports.extend(alias.name for alias in node.names)
                elif isinstance(node, ast.ImportFrom) and node.module:
                    imports.append(node.module)
            for module in imports:
                with self.subTest(module=name, dependency=module):
                    self.assertFalse(set(module.split('.')) & {'http', 'urllib', 'requests', 'service', 'reviews'})

    def test_model_adapter_does_not_implement_another_optimizer_or_generation_loop(self):
        for path in (self.source / 'runtime_qwen35').glob('*.py'):
            tree = ast.parse(path.read_text(encoding='utf-8-sig'))
            for node in ast.walk(tree):
                if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute):
                    with self.subTest(file=path.name, line=node.lineno):
                        self.assertNotIn(node.func.attr, {'backward', 'step', 'generate'})

    def test_public_renderer_alias_and_full_runtime_fingerprint_are_preserved(self):
        from coach_ml.environment import runtime_code_hashes
        from coach_ml.tactical.policy_v2 import render_target as legacy
        from coach_ml.tactical.target_rendering import render_target
        self.assertIs(legacy, render_target)
        hashes = runtime_code_hashes()
        for name in ('core/reporting.py', 'runtime/ports.py', 'tactical/target_rendering.py',
                     'tactical/policy_v2.py', 'tactical/error_analysis.py'):
            self.assertIn(name, hashes)


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
