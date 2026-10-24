import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from coach_ml.core.input_snapshot import capture_inputs, verify_inputs
from coach_ml.core.json import canonical
from coach_ml.core.reporting import write_report
from coach_ml.core.validation import AuditError


class AuditInfrastructureTests(unittest.TestCase):
    def test_complete_canonical_report_is_published_without_replacement(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'report.json'
            value = {'text': 'âncora', 'rows': [1, 2]}
            write_report(output, value)
            expected = (canonical(value) + '\n').encode('utf-8')
            self.assertEqual(output.read_bytes(), expected)
            with self.assertRaises(FileExistsError): write_report(output, {})
            self.assertEqual(output.read_bytes(), expected)
            self.assertEqual(list(Path(folder).iterdir()), [output])

    def test_serialization_and_flush_failures_leave_no_final_or_temporary_report(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'report.json'
            with self.assertRaises(TypeError): write_report(output, object())
            with patch('coach_ml.core.reporting.os.fsync', side_effect=OSError('disk full')):
                with self.assertRaises(OSError): write_report(output, {})
            self.assertEqual(list(Path(folder).iterdir()), [])

    def test_concurrent_destination_creation_is_preserved(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'report.json'
            link = os.link
            def race(source, destination):
                output.write_bytes(b'other writer')
                return link(source, destination)
            with patch('coach_ml.core.reporting.os.link', side_effect=race):
                with self.assertRaises(FileExistsError): write_report(output, {})
            self.assertEqual(output.read_bytes(), b'other writer')
            self.assertEqual(list(Path(folder).iterdir()), [output])

    def test_unsupported_link_fails_closed_and_snapshot_detects_changes(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'report.json'
            with patch('coach_ml.core.reporting.os.link', side_effect=OSError('unsupported filesystem')):
                with self.assertRaises(OSError): write_report(output, {})
            self.assertEqual(list(Path(folder).iterdir()), [])
            source = Path(folder) / 'input.json'
            source.write_text('first')
            files = {'input': source}
            expected = capture_inputs(files)
            verify_inputs(files, expected, 'changed')
            source.write_text('second')
            with self.assertRaisesRegex(AuditError, 'changed'): verify_inputs(files, expected, 'changed')
