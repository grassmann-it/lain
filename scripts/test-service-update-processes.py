#!/usr/bin/env python3
"""Isolate the active scanner AST; no live proc, service, provider or store access."""
import ast
import os
import subprocess
import selectors
import time
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import Mock

SOURCE = Path(__file__).with_name('service-update.py')


def need(value, message):
    if not value:
        raise RuntimeError(message)


def scanner(comm='ssh-agent', executable=None, arguments=b'/usr/bin/ssh-agent\0', privileged=False, resolved='/usr/bin/ssh-agent'):
    tree = ast.parse(SOURCE.read_text())
    prepare = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'prepare_update_class')
    function = next(n for n in prepare.body if isinstance(n, ast.FunctionDef) and n.name == 'processes')
    entry = Mock()
    entry.name = '39853'
    entry.stat.return_value.st_uid = 1000
    fields = {}
    for name, text in [('comm', comm), ('cmdline', arguments)]:
        fields[name] = Mock()
        fields[name].read_text.return_value = text
        fields[name].read_bytes.return_value = text
    entry.__truediv__ = Mock(side_effect=lambda name: fields.get(name, Path('/synthetic') / name))
    proc = Mock()
    proc.iterdir.return_value = [entry]
    path = Mock(side_effect=lambda value: proc if value == '/proc' else Path(value))
    readlink = Mock(side_effect=PermissionError()) if executable is None else Mock(return_value=executable)
    proof = SimpleNamespace(proves_platform_daemon=Mock(return_value=False))
    fields['privileged'] = Mock(return_value=resolved)
    fields['identity'] = Mock(return_value=(1000, 10))
    namespace = dict(process_identity=fields['identity'], args=SimpleNamespace(privileged_process_inspection=privileged),
                     privileged_process_executable=fields['privileged'], Path=path, STATE=Path('/synthetic/state'), need=need, proof=proof,
                     os=SimpleNamespace(getpid=lambda: 1, getuid=lambda: 1000,
                                        readlink=readlink, fsdecode=os.fsdecode))
    exec(compile(ast.Module(body=[function], type_ignores=[]), str(SOURCE), 'exec'), namespace)
    return namespace['processes'], proof, fields


class ProcessIdentityFixtures(unittest.TestCase):
    def test_unreadable_external_name_and_argv_are_not_identity(self):
        scan, proof, fields = scanner()
        fields['privileged'].side_effect = AssertionError('opt-in required')
        with self.assertRaisesRegex(RuntimeError, 'Unreadable own process cannot be classified'):
            scan()
        proof.proves_platform_daemon.assert_called_once()

    def test_own_names_never_use_external_exception(self):
        for comm in ('Lain.Host', 'Lain.Cli', 'Lain.Desktop', 'lain-proxy', 'dotnet'):
            with self.subTest(comm=comm):
                scan, proof, _ = scanner(comm=comm)
                proof.proves_platform_daemon.return_value = True
                with self.assertRaises(RuntimeError):
                    scan()
                proof.proves_platform_daemon.assert_not_called()

    def test_positive_external_proof_is_required(self):
        scan, proof, _ = scanner(comm='systemd')
        proof.proves_platform_daemon.return_value = True
        self.assertEqual(scan(), [])
        self.assertEqual(proof.proves_platform_daemon.call_args.args[1], ('/synthetic/state/app/',))

    def test_dotnet_dll_is_still_an_installed_writer(self):
        scan, _, _ = scanner(comm='dotnet', executable='/usr/bin/dotnet',
                             arguments=b'dotnet\0/synthetic/state/app/Lain.Host.dll\0')
        self.assertEqual(scan(), [(39853, '/synthetic/state/app/observed-process', False)])

    def test_renamed_desktop_dll_is_still_desktop(self):
        scan, _, _ = scanner(comm='renamed', executable='/usr/bin/dotnet',
                             arguments=b'dotnet\0/elsewhere/Lain.Desktop.dll\0')
        self.assertTrue(scan()[0][2])

    def test_unreadable_metadata_does_not_use_external_exception(self):
        scan, proof, fields = scanner(executable='/usr/bin/ssh-agent')
        fields['cmdline'].read_bytes.side_effect = PermissionError()
        with self.assertRaisesRegex(RuntimeError, 'Own process metadata unreadable'):
            scan()
        proof.proves_platform_daemon.assert_not_called()

    def test_privileged_exe_uses_normal_classification(self):
        for executable, arguments, installed, desktop in (
            ('/usr/bin/ssh-agent', b'ssh-agent\0', False, False),
            ('/usr/sbin/nginx', b'nginx\0', False, False),
            ('/synthetic/state/app/Lain.Host', b'host\0', True, False),
            ('/usr/bin/dotnet', b'dotnet\0/synthetic/state/app/Lain.Host.dll\0', True, False),
            ('/elsewhere/Lain.Desktop', b'desktop\0', False, True),
        ):
            with self.subTest(executable=executable):
                scan, proof, fields = scanner(privileged=True, resolved=executable, arguments=arguments)
                result = scan()
                self.assertEqual(result, [(39853, '/synthetic/state/app/observed-process' if installed else executable, desktop)])
                self.assertEqual(fields['privileged'].call_count, 2)
                proof.proves_platform_daemon.assert_not_called()

    def test_privileged_classification_detects_exec_and_metadata_changes(self):
        for change in ('exe', 'comm', 'argv', 'middle-argv', 'identity', 'disappeared'):
            scan, proof, fields = scanner(privileged=True)
            if change == 'exe': fields['privileged'].side_effect = ['/usr/bin/ssh-agent', '/usr/bin/dotnet']
            if change == 'comm': fields['comm'].read_text.side_effect = ['ssh-agent', 'ssh-agent', 'dotnet']
            if change == 'argv': fields['cmdline'].read_bytes.side_effect = [b'agent', b'agent', b'host']
            if change == 'middle-argv': fields['cmdline'].read_bytes.side_effect = [b'agent', b'host', b'agent']
            if change == 'identity': fields['identity'].side_effect = [(1000, 10), (1000, 11)]
            if change == 'disappeared': fields['privileged'].side_effect = ['/usr/bin/ssh-agent', FileNotFoundError()]
            with self.subTest(change=change), self.assertRaises(RuntimeError): scan()
            proof.proves_platform_daemon.assert_not_called()

    def test_privileged_failure_never_falls_back(self):
        scan, proof, fields = scanner(privileged=True)
        fields['privileged'].side_effect = RuntimeError('proof failed')
        proof.proves_platform_daemon.return_value = True
        with self.assertRaisesRegex(RuntimeError, 'proof failed'): scan()
        proof.proves_platform_daemon.assert_not_called()

    def test_readable_exe_never_invokes_sudo_helper(self):
        scan, _, fields = scanner(privileged=True, executable='/usr/bin/ssh-agent')
        scan()
        fields['privileged'].assert_not_called()

    def test_docker_cgroup_and_ssh_name_do_not_satisfy_finite_proof(self):
        source = SOURCE.with_name('service-update-platform.py')
        tree = ast.parse(source.read_text())
        function = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'proves_platform_daemon')
        group = '/user.slice/user-1000.slice/user@1000.service/init.scope'
        manager = dict(comm='systemd', arguments=('/usr/lib/systemd/systemd', '--user'),
                       cgroup='0::' + group)
        # A foreign agent and a renamed own host have identical readable metadata.
        # Neither argv nor Docker placement establishes executable identity.
        for argv in (('/usr/bin/ssh-agent',), ('/synthetic/state/app/Lain.Host',)):
            with self.subTest(argv=argv):
                candidate = dict(comm='ssh-agent', arguments=argv,
                                 cgroup='0::/system.slice/docker-synthetic.scope')
                env = dict(owner=lambda: (':1.2', 42, 1000), Path=Path, need=lambda value: value or (_ for _ in ()).throw(ValueError()),
                           snapshot=lambda entry, prefixes: manager if entry.name == '42' else candidate,
                           os=os, subprocess=subprocess)
                exec(compile(ast.Module(body=[function], type_ignores=[]), str(source), 'exec'), env)
                self.assertFalse(env['proves_platform_daemon'](Path('/synthetic/proc/39853'),
                                                            ('/synthetic/state/app/',)))

    def test_missing_own_argv_blocks(self):
        scan, _, _ = scanner(comm='dotnet', executable='/usr/bin/dotnet', arguments=b'')
        with self.assertRaisesRegex(RuntimeError, 'Application candidate argv unavailable'):
            scan()


class PrivilegedProofFixtures(unittest.TestCase):
    def fixture(self, chunks=(b'/usr/bin/ssh-agent\n', b''), identities=((1000, 10), (1000, 10))):
        tree = ast.parse(SOURCE.read_text())
        function = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'privileged_process_executable')
        child = Mock()
        child.__enter__ = Mock(side_effect=AssertionError('unbounded context exit'))
        child.wait.return_value = 0
        child.poll.return_value = 0
        selector = Mock()
        selector.__enter__ = Mock(return_value=selector)
        selector.__exit__ = Mock(return_value=False)
        selector.select.return_value = [True]
        spawn = Mock(return_value=child)
        identity = Mock(side_effect=identities)
        env = dict(need=need, re=__import__('re'), time=time,
                   process_identity=identity,
                   os=SimpleNamespace(getuid=lambda: 1000, read=Mock(side_effect=chunks), fsdecode=os.fsdecode),
                   selectors=SimpleNamespace(DefaultSelector=lambda: selector, EVENT_READ=selectors.EVENT_READ),
                   subprocess=SimpleNamespace(Popen=spawn, DEVNULL=subprocess.DEVNULL,
                                              PIPE=subprocess.PIPE, TimeoutExpired=subprocess.TimeoutExpired))
        exec(compile(ast.Module(body=[function], type_ignores=[]), str(SOURCE), 'exec'), env)
        return lambda: env['privileged_process_executable'](Path('/proc/39853')), spawn, child, selector, identity

    def test_exact_command_and_closed_input(self):
        run, spawn, _, _, identity = self.fixture()
        self.assertEqual(run(), '/usr/bin/ssh-agent')
        self.assertEqual(spawn.call_args.args[0], ['/usr/bin/sudo', '-n', '--', '/usr/bin/readlink', '--', '/proc/39853/exe'])
        self.assertEqual(spawn.call_args.kwargs['stdin'], subprocess.DEVNULL)
        self.assertEqual(spawn.call_args.kwargs['stderr'], subprocess.DEVNULL)
        self.assertNotIn('shell', spawn.call_args.kwargs)
        self.assertEqual(identity.call_count, 2)

    def test_reuse_uid_and_disappearance_block(self):
        for after in ((1000, 11), (1001, 10), FileNotFoundError(), PermissionError()):
            run, *_ = self.fixture(identities=((1000, 10), after))
            with self.subTest(after=after), self.assertRaises(RuntimeError): run()

    def test_wrong_initial_owner_never_spawns(self):
        run, spawn, *_ = self.fixture(identities=((1001, 10),))
        with self.assertRaises(RuntimeError): run()
        spawn.assert_not_called()

    def test_output_is_bounded_and_validated(self):
        for output in (b'x' * 4097, b'relative\n', b'/a\n/b\n', b'/a\0\n', b'/a', b''):
            run, *_ = self.fixture(chunks=(output, b''))
            with self.subTest(output=output[:20]), self.assertRaises(RuntimeError): run()

    def test_nonzero_timeout_and_empty_readiness_block(self):
        for failure in ('exit', 'wait', 'select'):
            run, _, child, selector, _ = self.fixture()
            if failure == 'exit': child.wait.return_value = 1
            if failure == 'wait': child.wait.side_effect = subprocess.TimeoutExpired('synthetic', 3)
            if failure == 'select':
                selector.select.return_value = []
                child.poll.return_value = None
            with self.subTest(failure=failure), self.assertRaises(RuntimeError): run()
            if failure == 'select': child.kill.assert_called_once()

    def test_cleanup_waits_are_bounded_even_when_reap_fails(self):
        for failure in ('timeout', 'kill', 'read'):
            run, _, child, selector, _ = self.fixture()
            selector.select.return_value = []
            child.poll.return_value = None
            if failure == 'timeout': child.wait.side_effect = subprocess.TimeoutExpired('synthetic', .25)
            if failure == 'kill': child.kill.side_effect = OSError('synthetic')
            if failure == 'read': selector.select.side_effect = OSError('synthetic')
            with self.subTest(failure=failure), self.assertRaises(RuntimeError): run()
            child.stdout.close.assert_called_once()
            child.__enter__.assert_not_called()
            for call in child.wait.call_args_list:
                self.assertGreater(call.kwargs['timeout'], 0)
                self.assertLessEqual(call.kwargs['timeout'], .25)

    def test_spawn_time_counts_toward_deadline(self):
        run, _, child, selector, _ = self.fixture()
        # The isolated helper's globals are shared through the closure's function.
        function = run.__closure__[0].cell_contents['privileged_process_executable']
        function.__globals__['time'] = SimpleNamespace(monotonic=Mock(side_effect=[0, 4]))
        child.poll.return_value = None
        with self.assertRaises(RuntimeError): run()
        selector.select.assert_not_called()
        child.kill.assert_called_once()
        child.wait.assert_called_once_with(timeout=.25)
        child.stdout.close.assert_called_once()

    def test_stat_starttime_parser_handles_parentheses(self):
        tree = ast.parse(SOURCE.read_text())
        function = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'process_identity')
        env = dict(need=need)
        exec(compile(ast.Module(body=[function], type_ignores=[]), str(SOURCE), 'exec'), env)
        proc = Mock()
        proc.stat.return_value.st_uid = 1000
        field = Mock()
        field.read_text.return_value = '39853 (name ) with spaces) ' + ' '.join(['S'] + ['0'] * 18 + ['12345'])
        proc.__truediv__ = Mock(return_value=field)
        self.assertEqual(env['process_identity'](proc), (1000, 12345))


if __name__ == '__main__':
    unittest.main()
