from types import SimpleNamespace
import unittest

from coach_ml.audit import AuditError
from coach_ml.environment import runtime_environment


def installed(name, version):
    return SimpleNamespace(metadata={'Name': name}, version=version)


class EnvironmentTests(unittest.TestCase):
    def test_installed_iteration_order_and_package_name_aliases_are_stable(self):
        first = runtime_environment([installed('Some_Package', '1'), installed('Torch', '2')])
        second = runtime_environment([installed('torch', '2'), installed('some-package', '1')])
        self.assertEqual(first, second)
        self.assertEqual(first['packages'], {'some-package': '1', 'torch': '2'})

    def test_changed_dependency_produces_a_different_environment_fingerprint(self):
        before = runtime_environment([installed('transformers', '4.56.2')])
        after = runtime_environment([installed('transformers', '4.57.0')])
        self.assertNotEqual(before, after)

    def test_conflicting_duplicate_distributions_are_rejected(self):
        with self.assertRaisesRegex(AuditError, 'Conflicting'):
            runtime_environment([installed('Some.Package', '1'), installed('some-package', '2')])
