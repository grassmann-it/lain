#!/usr/bin/env python3
"""Test only AST-selected guards from an explicit hashed private helper.

Never import the helper, run its top level, or call service/process/update code.
Only pure validation and two bounded methods use disposable files and fakes.
"""
import argparse
import ast
import hashlib
import json
from pathlib import Path
import re
import shlex
import sys
import tempfile
from types import SimpleNamespace
import unittest

HERE = Path(__file__).resolve().parent
SOURCE = None


class UpdaterGuards(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix='lain-updater-guards-')
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        tree = ast.parse(SOURCE)
        definitions = [node for node in tree.body if isinstance(node, ast.FunctionDef)
                       and node.name in ('need', 'validate_unit_overrides')]
        original = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == 'Update')
        methods = [node for node in original.body if isinstance(node, ast.FunctionDef)
                   and node.name in ('run', 'require_previous_app')]
        self.assertEqual({node.name for node in definitions}, {'need', 'validate_unit_overrides'})
        self.assertEqual({node.name for node in methods}, {'run', 'require_previous_app'})
        bounded_class = ast.ClassDef(name='Update', bases=[], keywords=[], body=methods, decorator_list=[])
        selected = ast.fix_missing_locations(ast.Module(body=[*definitions, bounded_class], type_ignores=[]))
        self.namespace = {'STATE': self.root, 'Path': Path, 'shlex': shlex, 'json': json, 're': re}
        # No import or top-level code from the helper is evaluated. subprocess is
        # injected per test as a fake object, never the real subprocess module.
        exec(compile(selected, '<selected-private-updater-guards>', 'exec'), self.namespace)
        self.update = self.namespace['Update']()
        self.update.guard = lambda: None
        self.update.env = {}
        self.update.backup = self.root

    def test_loaded_unit_fixture(self):
        fixture = json.loads((HERE / 'fixtures/updater-unit-guards.json').read_text())
        for case in fixture['cases']:
            with self.subTest(case=case['name']):
                values = dict(fixture['base'], **case['replace'])
                values = {key: value.replace('{STATE}', str(self.root)) for key, value in values.items()}
                for key in case.get('remove', []):
                    del values[key]
                if case['accept']:
                    self.namespace['validate_unit_overrides'](values)
                else:
                    with self.assertRaises(RuntimeError):
                        self.namespace['validate_unit_overrides'](values)

    def test_failed_child_diagnostics_exclude_output_and_arguments(self):
        marker = 'SYNTHETIC-PRIVATE-OUTPUT-DO-NOT-PERSIST'
        self.namespace['subprocess'] = SimpleNamespace(run=lambda *a, **kw: SimpleNamespace(
            returncode=23, stderr=marker.encode(), stdout=marker.encode()))
        with self.assertRaises(RuntimeError) as error:
            self.update.run(['/synthetic/bin/python3', '--private-argument=' + marker])
        content = (self.root / 'private-errors.log').read_text()
        self.assertEqual(json.loads(content), {'command': 'python3', 'exitCode': 23})
        self.assertNotIn(marker, content + str(error.exception))
        self.assertNotIn('private-argument', content + str(error.exception))

    def test_successful_child_output_is_returned_without_diagnostic_file(self):
        self.namespace['subprocess'] = SimpleNamespace(run=lambda *a, **kw: SimpleNamespace(
            returncode=0, stderr=b'', stdout=b'synthetic-result'))
        self.assertEqual(self.update.run(['/synthetic/bin/python3']), b'synthetic-result')
        self.assertFalse((self.root / 'private-errors.log').exists())

    def test_unusual_command_identity_is_fixed_not_raw(self):
        self.namespace['subprocess'] = SimpleNamespace(run=lambda *a, **kw: SimpleNamespace(
            returncode=1, stderr=b'', stdout=b''))
        with self.assertRaises(RuntimeError):
            self.update.run(['/synthetic/unsafe\ncommand'])
        self.assertEqual(json.loads((self.root / 'private-errors.log').read_text())['command'], 'unrecognized-command')

    def prepare_app_check(self):
        app = self.root / 'app'; app.mkdir()
        payload = app / 'synthetic-payload'; payload.write_bytes(b'old reviewed application')
        digest = lambda: hashlib.sha256(payload.read_bytes()).hexdigest()
        pin = digest()
        calls = []

        def package_info(path, manifest, mode, core):
            calls.append((path, manifest, mode, core))
            if path != app or digest() != manifest:
                raise RuntimeError('Synthetic complete package pin differs')
            return {'manifestSha256': manifest, 'coreSha256': core}

        self.update.args = SimpleNamespace(previous_gateway_manifest_sha256=pin, previous_core_sha256='a' * 64)
        self.update.plan = {'packages': {'previousGateway': {'manifestSha256': pin, 'coreSha256': 'a' * 64}}}
        self.update.verifier = {'actual': SimpleNamespace(package_info=package_info)}
        return payload, calls

    def test_previous_app_repin_accepts_unchanged_files(self):
        _, calls = self.prepare_app_check()
        self.update.require_previous_app()
        self.assertEqual(len(calls), 1)

    def test_previous_app_repin_rejects_change_after_first_read(self):
        payload, _ = self.prepare_app_check()
        self.update.require_previous_app()
        payload.write_bytes(b'changed while stopped snapshot was taken')
        with self.assertRaises(RuntimeError):
            self.update.require_previous_app()

    def test_previous_app_repin_rejects_different_reviewed_plan(self):
        self.prepare_app_check()
        self.update.plan['packages']['previousGateway']['coreSha256'] = 'b' * 64
        with self.assertRaises(RuntimeError):
            self.update.require_previous_app()

    def test_repin_is_immediately_before_actual_installer(self):
        tree = ast.parse(SOURCE)
        original = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == 'Update')
        execute = next(node for node in original.body if isinstance(node, ast.FunctionDef) and node.name == 'execute')
        repins = [node for node in ast.walk(execute) if isinstance(node, ast.Call)
                  and isinstance(node.func, ast.Attribute) and node.func.attr == 'require_previous_app']
        self.assertEqual(len(repins), 2)
        located = []
        for node in ast.walk(execute):
            for field, statements in ast.iter_fields(node):
                if not isinstance(statements, list):
                    continue
                for index, statement in enumerate(statements):
                    if not isinstance(statement, ast.Expr) or not isinstance(statement.value, ast.Call):
                        continue
                    call = statement.value
                    if not isinstance(call.func, ast.Attribute) or call.func.attr != 'run' or not call.args:
                        continue
                    command = call.args[0]
                    if isinstance(command, ast.List) and command.elts and isinstance(command.elts[0], ast.Constant) and command.elts[0].value == 'bash':
                        previous = statements[index - 1] if index else None
                        located.append(isinstance(previous, ast.Expr) and isinstance(previous.value, ast.Call)
                                       and isinstance(previous.value.func, ast.Attribute)
                                       and previous.value.func.attr == 'require_previous_app')
        self.assertEqual(located, [True])

    def test_loaded_unit_reads_all_hooks_and_invokes_override_guard(self):
        tree = ast.parse(SOURCE)
        update = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == 'Update')
        unit = next(node for node in update.body if isinstance(node, ast.FunctionDef) and node.name == 'own_unit')
        strings = {node.value for node in ast.walk(unit) if isinstance(node, ast.Constant) and isinstance(node.value, str)}
        for hook in ('ExecStartPre', 'ExecStartPost', 'ExecStop', 'ExecStopPost'):
            self.assertIn('--property=' + hook, strings)
        self.assertTrue(any(isinstance(node, ast.Call) and isinstance(node.func, ast.Name)
                            and node.func.id == 'validate_unit_overrides' for node in ast.walk(unit)))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--helper', type=Path, required=True)
    parser.add_argument('--helper-sha256', required=True)
    args, rest = parser.parse_known_args()
    data = args.helper.read_bytes()
    if hashlib.sha256(data).hexdigest() != args.helper_sha256:
        parser.error('Private helper SHA-256 does not match the reviewed source pin')
    SOURCE = data.decode()
    unittest.main(argv=[sys.argv[0], *rest])
