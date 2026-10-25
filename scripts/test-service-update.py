#!/usr/bin/env python3
"""Prepared isolated drain fixtures; Root executes after source review.

Compiles only the nested Update class definitions from operator AST against
mocked primitives. Never imports operator/helpers, reads tokens, or touches /proc,
network, services, installed data, packages, or real backup destinations.
"""
import ast
import http.client
import json
import tempfile
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

SOURCE = Path(__file__).with_name('service-update.py')


def need(value, message):
    if not value: raise RuntimeError(message)


def prepared_class():
    tree = ast.parse(SOURCE.read_text())
    function = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == 'run_update')
    definition = next(node for node in function.body if isinstance(node, ast.ClassDef) and node.name == 'Update')
    isolated = ast.Module(body=[definition], type_ignores=[])
    namespace = {'old': SimpleNamespace(Update=object), 'before_files': {}, 'need': need,
                 'http': SimpleNamespace(client=http.client), 'json': json, 'time': SimpleNamespace(
                     monotonic=Mock(return_value=10), sleep=Mock())}
    exec(compile(isolated, str(SOURCE), 'exec'), namespace)
    return namespace['Update'], namespace


class DrainFixtures(unittest.TestCase):
    def setUp(self):
        self.cls, self.env = prepared_class()
        self.update = object.__new__(self.cls)
        self.update.drain_attempted = False
        self.update.drain_identity = None
        self.identity = {'host': {'pid': 1, 'start': 10, 'sha256': 'h'},
                         'core': {'pid': 2, 'start': 20, 'sha256': 'c'}}
        self.update.backend_identity = Mock(return_value=self.identity)
        self.update.management_token = Mock(return_value='synthetic-test-token')
        self.update.guard = Mock()
        self.update.activity_request = Mock()

    def test_waits_for_existing_stream_to_finish(self):
        self.update.activity_request.side_effect = [
            {'draining': False, 'activeRequests': 2}, None,
            {'draining': True, 'activeRequests': 1}, {'draining': True, 'activeRequests': 0}]
        self.update.drain()
        self.assertTrue(self.update.drain_attempted)
        self.assertEqual([call.args[1:3] for call in self.update.activity_request.call_args_list],
                         [('GET', '/lain/activity'), ('POST', '/lain/drain'),
                          ('GET', '/lain/activity'), ('GET', '/lain/activity')])
        self.env['time'].sleep.assert_called_once()

    def test_preexisting_barrier_is_never_adopted_or_opened(self):
        self.update.activity_request.return_value = {'draining': True, 'activeRequests': 0}
        with self.assertRaises(RuntimeError): self.update.drain()
        self.assertFalse(self.update.drain_attempted)
        self.assertEqual(self.update.activity_request.call_count, 1)

    def test_lost_post_response_retains_cleanup_obligation(self):
        self.update.activity_request.side_effect = [{'draining': False, 'activeRequests': 0}, OSError('lost')]
        with self.assertRaises(OSError): self.update.drain()
        self.assertTrue(self.update.drain_attempted)
        self.update.activity_request.side_effect = [None, {'draining': False, 'activeRequests': 0}]
        self.update.restore_admission()
        self.assertFalse(self.update.drain_attempted)

    def test_deadline_never_stops_active_stream(self):
        self.update.activity_request.side_effect = [{'draining': False, 'activeRequests': 3}, None]
        self.env['time'].monotonic.side_effect = [10, 31]
        with self.assertRaises(RuntimeError): self.update.drain()
        self.assertTrue(self.update.drain_attempted)
        self.assertEqual(self.update.activity_request.call_count, 2)

    def test_disappearing_barrier_refuses(self):
        self.update.activity_request.side_effect = [
            {'draining': False, 'activeRequests': 0}, None, {'draining': False, 'activeRequests': 0}]
        with self.assertRaises(RuntimeError): self.update.drain()

    def test_changed_core_after_drain_refuses(self):
        self.update.backend_identity.side_effect = [self.identity, self.identity, {'core': 'changed'}]
        self.update.activity_request.side_effect = [
            {'draining': False, 'activeRequests': 0}, None, {'draining': True, 'activeRequests': 0}]
        with self.assertRaises(RuntimeError): self.update.drain()

    def test_changed_backend_is_not_opened_during_cleanup(self):
        self.update.drain_identity = self.identity
        self.update.backend_identity.return_value = {'core': 'different'}
        with self.assertRaises(RuntimeError): self.update.restore_admission()
        self.update.activity_request.assert_not_called()

    def test_lost_delete_response_requires_independent_get(self):
        self.update.drain_identity = self.identity
        self.update.drain_attempted = True
        self.update.activity_request.side_effect = [OSError('lost'), {'draining': False, 'activeRequests': 2}]
        self.update.restore_admission()
        self.assertFalse(self.update.drain_attempted)
        self.assertEqual(self.update.backend_identity.call_args.kwargs, {'allow_ui': True})

    def test_unconfirmed_reopen_remains_failure(self):
        self.update.drain_identity = self.identity
        self.update.drain_attempted = True
        self.update.activity_request.side_effect = [None, {'draining': True, 'activeRequests': 0}]
        with self.assertRaises(RuntimeError): self.update.restore_admission()
        self.assertTrue(self.update.drain_attempted)

    def test_barrier_rechecked_immediately_before_stop(self):
        self.update.drain_identity = self.identity
        for body in ({'draining': False, 'activeRequests': 0}, {'draining': True, 'activeRequests': 1}):
            with self.subTest(body=body):
                self.update.activity_request.return_value = body
                with self.assertRaises(RuntimeError): self.update.confirm_drain()
        self.update.activity_request.return_value = {'draining': True, 'activeRequests': 0}
        self.update.confirm_drain()

    def failure_operator(self, root):
        self.env.update(Path=Path, STATE=root / 'home', config={'package': {'path': str(root / 'package')}},
                        threading=SimpleNamespace(Thread=Mock(return_value=Mock())))
        update = self.update
        update.backup = root / 'backup'
        update.record = {}
        update.touched = False
        update.save = Mock()
        update.watch = Mock()
        update.watch_done = Mock()
        update.own_unit = Mock(side_effect=RuntimeError('original precise failure text'))
        return update

    def test_original_update_failure_is_retained_and_chained(self):
        with tempfile.TemporaryDirectory() as tmp:
            update = self.failure_operator(Path(tmp))
            with self.assertRaisesRegex(RuntimeError, 'original precise failure text') as raised:
                update.execute()
            self.assertEqual(update.record['errorMessage'], 'original precise failure text')
            self.assertEqual(update.record['phase'], 'failed-before-stop')
            self.assertIsInstance(raised.exception.__cause__, RuntimeError)

    def test_diagnostic_write_failure_does_not_replace_original_failure(self):
        with tempfile.TemporaryDirectory() as tmp:
            update = self.failure_operator(Path(tmp))
            update.save.side_effect = [None, OSError('synthetic evidence disk failure')]
            with self.assertRaisesRegex(RuntimeError, 'original precise failure text.*synthetic evidence disk failure'):
                update.execute()

    def response(self, body, status=200):
        response = Mock(status=status)
        response.read.return_value = json.dumps(body).encode()
        connection = Mock()
        connection.getresponse.return_value = response
        return connection

    def test_fixed_socket_and_bearer_are_only_in_memory(self):
        connection = self.response({'draining': False, 'activeRequests': 0})
        with patch.object(http.client, 'HTTPConnection', return_value=connection) as connect:
            result = self.cls.activity_request(self.update, 'synthetic', 'GET', '/lain/activity', 2)
        connect.assert_called_once_with('127.0.0.1', 18417, timeout=2)
        connection.request.assert_called_once_with('GET', '/lain/activity', headers={'Authorization': 'Bearer synthetic'})
        self.assertEqual(result['activeRequests'], 0)
        connection.close.assert_called_once()

    def test_redirect_is_not_followed(self):
        connection = self.response({}, status=302)
        with patch.object(http.client, 'HTTPConnection', return_value=connection):
            with self.assertRaises(RuntimeError):
                self.cls.activity_request(self.update, 'synthetic', 'GET', '/lain/activity', 2)
        self.assertEqual(connection.request.call_count, 1)
        connection.close.assert_called_once()

    def test_malformed_activity_fails_closed(self):
        for body in ({'draining': True, 'activeRequests': -1}, {'draining': True, 'activeRequests': False},
                     {'draining': 1, 'activeRequests': 0}, {'activeRequests': 0}):
            with self.subTest(body=body):
                connection = self.response(body)
                with patch.object(http.client, 'HTTPConnection', return_value=connection):
                    with self.assertRaises(RuntimeError):
                        self.cls.activity_request(self.update, 'synthetic', 'GET', '/lain/activity', 2)


if __name__ == '__main__':
    unittest.main()
