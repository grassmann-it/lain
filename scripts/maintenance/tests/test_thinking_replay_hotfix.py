#!/usr/bin/env python3
"""Offline qualification: only a private local stub receives synthetic requests."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import queue
import socket
import subprocess
import tempfile
import threading
import time
import unittest
from unittest import mock
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import yaml

SCRIPT = Path(__file__).resolve().parents[1] / 'thinking_replay_hotfix.py'
spec = importlib.util.spec_from_file_location('hotfix', SCRIPT)
hotfix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hotfix)
BINARY = Path('/home/tomka/.local/share/easycliproxyapi/cpa-core/cli-proxy-api')
ALIAS = 'cmd-claude-sonnet-5-5'


def route(base='https://api.commandcode.ai/provider'):
    return {'base-url': base, 'api-key': 'synthetic-test-key', 'cloak': {'mode': 'never'},
            'models': [{'name': 'claude-sonnet-5-5', 'alias': ALIAS, 'is-compat': True,
                        'thinking': {'levels': ['low', 'medium', 'high', 'xhigh', 'max']}}]}


class ConfigTests(unittest.TestCase):
    def test_scoped_idempotent_and_guarded_rollback(self):
        initial = {'claude-api-key': [route()], 'payload': {'default': [{'models': [{'name': 'unrelated'}], 'params': {'x': 1}}]}}
        raw = yaml.safe_dump(initial).encode()
        patched, names = hotfix.patched_bytes(raw)
        self.assertEqual(names, [ALIAS])
        self.assertEqual(hotfix.patched_bytes(patched)[0], patched)
        self.assertEqual(yaml.safe_load(patched)['payload']['default'][0], initial['payload']['default'][0])
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'config.yaml'
            path.write_bytes(raw)
            result = hotfix.apply(path, hotfix.digest(raw), Path(temporary) / 'backups', admin_lock=Path(temporary) / 'admin.lock')
            self.assertEqual(path.stat().st_mode & 0o777, 0o600)
            with self.assertRaises(ValueError):
                hotfix.apply(path, hotfix.digest(raw), Path(temporary) / 'backups', admin_lock=Path(temporary) / 'admin.lock')
            hotfix.rollback(result['receipt'], admin_lock=Path(temporary) / 'admin.lock')
            self.assertEqual(path.read_bytes(), raw)

    def test_alias_collisions_and_later_thinking_mutations_are_refused(self):
        account = route()
        del account['models'][0]['alias']
        with self.assertRaises(ValueError):
            hotfix.patched_bytes(yaml.safe_dump({'claude-api-key': [account]}).encode())
        collision = route('https://other.invalid')
        with self.assertRaises(ValueError):
            hotfix.patched_bytes(yaml.safe_dump({'claude-api-key': [route(), collision]}).encode())
        config = {'claude-api-key': [route()], 'payload': {'override': [{'models': [{'name': ALIAS}], 'params': {'thinking.type': 'disabled'}}]}}
        with self.assertRaises(ValueError):
            hotfix.patched_bytes(yaml.safe_dump(config).encode())

    def test_inplace_io_failure_has_no_retry_and_reports_recovery(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'config.yaml'
            path.write_bytes(b'original: true\n')
            with mock.patch.object(hotfix.os, 'pwrite', side_effect=OSError('synthetic IO failure')) as write:
                with self.assertRaises(hotfix.RecoveryRequired):
                    hotfix.preserve_inode_write(path, hotfix.digest(path.read_bytes()), b'changed: true\n', 'synthetic-receipt')
                self.assertEqual(write.call_count, 1)
            self.assertEqual(path.read_bytes(), b'original: true\n')

    def test_binary_mismatch_is_refused(self):
        with tempfile.TemporaryDirectory() as temporary:
            binary = Path(temporary) / 'fake-binary'
            binary.write_bytes(b'not-the-qualified-proxy')
            with self.assertRaises(ValueError):
                hotfix.check_binary(binary)

    def test_no_other_provider_or_model(self):
        for entry in [route('https://other.invalid'), {'base-url': 'https://api.commandcode.ai/provider', 'models': [{'name': 'claude-haiku-5-5', 'is-compat': True}]}]:
            with self.assertRaises(ValueError):
                hotfix.patched_bytes(yaml.safe_dump({'claude-api-key': [entry]}).encode())


class InstalledBinaryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if hotfix.digest(BINARY.read_bytes()) != hotfix.QUALIFIED_BINARY_SHA256:
            raise RuntimeError('installed binary differs from qualified hash')
        cls.directory = tempfile.TemporaryDirectory(prefix='thinking-replay-qualification-')
        cls.root = Path(cls.directory.name)
        cls.received = queue.Queue()
        received = cls.received
        cls.hold_started = threading.Event()
        cls.hold_release = threading.Event()
        hold_started, hold_release = cls.hold_started, cls.hold_release

        class Stub(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
                received.put((body, dict(self.headers)))
                self.send_response(200)
                if body.get('stream'):
                    self.send_header('Content-Type', 'text/event-stream')
                    self.end_headers()
                    events = [
                        ('message_start', {'type': 'message_start', 'message': {'id': 'msg_test', 'type': 'message', 'role': 'assistant', 'model': 'claude-sonnet-5-5', 'content': [], 'usage': {'input_tokens': 1, 'output_tokens': 0}}}),
                        ('content_block_start', {'type': 'content_block_start', 'index': 0, 'content_block': {'type': 'text', 'text': ''}}),
                        ('content_block_delta', {'type': 'content_block_delta', 'index': 0, 'delta': {'type': 'text_delta', 'text': 'ok'}}),
                        ('content_block_stop', {'type': 'content_block_stop', 'index': 0}),
                        ('message_delta', {'type': 'message_delta', 'delta': {'stop_reason': 'end_turn'}, 'usage': {'output_tokens': 1}}),
                        ('message_stop', {'type': 'message_stop'})]
                    for event, payload in events:
                        self.wfile.write(('event: ' + event + '\ndata: ' + json.dumps(payload) + '\n\n').encode())
                        self.wfile.flush()
                        if event == 'message_start' and 'hold-stream' in json.dumps(body):
                            hold_started.set()
                            hold_release.wait(10)
                else:
                    self.send_header('Content-Type', 'application/json')
                    self.end_headers()
                    self.wfile.write(json.dumps({'id': 'msg_test', 'type': 'message', 'role': 'assistant', 'model': 'claude-sonnet-5-5', 'content': [{'type': 'text', 'text': 'ok'}], 'stop_reason': 'end_turn', 'usage': {'input_tokens': 1, 'output_tokens': 1}}).encode())

        cls.stub = ThreadingHTTPServer(('127.0.0.1', 0), Stub)
        threading.Thread(target=cls.stub.serve_forever, daemon=True).start()
        with socket.socket() as reserve:
            reserve.bind(('127.0.0.1', 0))
            cls.port = reserve.getsockname()[1]
        account = route()
        opus = copy.deepcopy(account['models'][0])
        opus['name'], opus['alias'] = 'claude-opus-5-5', 'cmd-claude-opus-5-5'
        account['models'].append(opus)
        config = {'host': '127.0.0.1', 'port': cls.port, 'auth-dir': str(cls.root / 'auth'),
                  'api-keys': ['synthetic-local-key'], 'claude-api-key': [account],
                  'remote-management': {'disable-control-panel': True},
                  'request-retry': 0, 'plugins': {'enabled': False}, 'usage-statistics-enabled': False,
                  'payload': {'default': [
                      {'models': [{'name': ALIAS, 'protocol': 'claude', 'headers': {'X-Test-Array': 'yes'}}], 'params': {'betas': ['synthetic-body-beta']}},
                      {'models': [{'name': ALIAS, 'protocol': 'claude', 'headers': {'X-Test-Existing': 'yes'}}], 'params': {'betas': [hotfix.BETA, 'synthetic-body-beta']}},
                      {'models': [{'name': ALIAS, 'protocol': 'claude', 'headers': {'X-Test-Policy': 'yes'}}], 'params': {hotfix.POLICY: 'error'}},
                  ]}}
        # Prepare rules from the qualified real provider URL, then redirect only the
        # synthetic test account to loopback. No real config/auth is loaded.
        config = yaml.safe_load(hotfix.patched_bytes(yaml.safe_dump(config).encode())[0])
        config['claude-api-key'][0]['base-url'] = 'http://127.0.0.1:' + str(cls.stub.server_port)
        cls.config = config
        cls.config_path = cls.root / 'config.yaml'
        cls.config_path.write_text(yaml.safe_dump(config))
        (cls.root / 'auth').mkdir(mode=0o700)
        cls.log = open(cls.root / 'proxy.log', 'wb')
        cls.process = subprocess.Popen([str(BINARY), '--local-model', '--config', str(cls.config_path)], cwd=cls.root, stdout=cls.log, stderr=cls.log)
        for _ in range(100):
            if cls.process.poll() is not None:
                raise RuntimeError('isolated binary exited; inspect private test log')
            try:
                request = urllib.request.Request('http://127.0.0.1:' + str(cls.port) + '/v1/models', headers={'Authorization': 'Bearer synthetic-local-key'})
                urllib.request.urlopen(request, timeout=1).close()
                if 'file watcher started' in (cls.root / 'proxy.log').read_text(errors='replace'):
                    break
                time.sleep(.05)
            except OSError:
                time.sleep(.05)
        else:
            raise RuntimeError('isolated binary not ready')

    @classmethod
    def tearDownClass(cls):
        cls.process.terminate()
        try:
            cls.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            cls.process.kill()
            cls.process.wait()
        cls.log.close()
        cls.stub.shutdown()
        cls.stub.server_close()
        cls.directory.cleanup()

    def send(self, body, path='/v1/responses', extra_headers=None):
        request = urllib.request.Request('http://127.0.0.1:' + str(self.port) + path, data=json.dumps(body).encode(), headers={'Content-Type': 'application/json', 'Authorization': 'Bearer synthetic-local-key', 'Anthropic-Beta': 'synthetic-existing-beta', **(extra_headers or {})})
        with urllib.request.urlopen(request, timeout=10) as response:
            response.read()
        return self.received.get(timeout=2)

    def payload(self, stream=False, signature='synthetic-opaque-signature', effort='high', tool=False):
        items = [{'role': 'user', 'content': 'synthetic question'}, {'type': 'reasoning', 'encrypted_content': signature, 'summary': [{'type': 'summary_text', 'text': 'synthetic summary'}]}, {'role': 'assistant', 'content': [{'type': 'output_text', 'text': 'synthetic answer'}]}]
        if tool:
            items += [{'type': 'function_call', 'call_id': 'call_test', 'name': 'test_tool', 'arguments': '{}'}, {'type': 'function_call_output', 'call_id': 'call_test', 'output': 'synthetic result'}]
        else:
            items += [{'role': 'user', 'content': 'synthetic followup'}]
        body = {'model': ALIAS, 'input': items, 'stream': stream}
        if effort is not None:
            body['reasoning'] = {'effort': effort}
        return body

    def test_adaptive_stream_and_nonstream_and_tool_history(self):
        for stream, tool, signature in [(False, False, 'synthetic-opaque-signature'), (True, False, 'synthetic-opaque-signature'), (True, True, 'synthetic-opaque-signature'), (False, True, 'claude-redacted-thinking:synthetic-redacted')]:
            with self.subTest(stream=stream, tool=tool, redacted=signature.startswith('claude-redacted')):
                body, headers = self.send(self.payload(stream, signature, tool=tool))
                self.assertEqual(body['thinking']['block_binding']['prefix_mismatch_behavior'], 'drop_block')
                betas = headers.get('Anthropic-Beta', '').split(',')
                self.assertIn(hotfix.BETA, betas)
                self.assertIn('synthetic-existing-beta', betas)
                parts = [part for msg in body['messages'] for part in msg.get('content', []) if isinstance(part, dict)]
                if signature.startswith('claude-redacted'):
                    self.assertTrue(any(part.get('type') == 'redacted_thinking' and part.get('data') == 'synthetic-redacted' for part in parts))
                else:
                    self.assertTrue(any(part.get('signature') == signature for part in parts))
                if tool:
                    self.assertTrue(any(part.get('type') == 'tool_use' for part in parts))
                    self.assertTrue(any(part.get('type') == 'tool_result' for part in parts))

    def test_existing_beta_arrays_and_explicit_policy_survive(self):
        # Initial config defaults seed translated fields before hotfix defaults.
        # Responses has no native thinking.block_binding field to preserve itself.
        for header in ['X-Test-Array', 'X-Test-Existing']:
            body, headers = self.send(self.payload(), extra_headers={header: 'yes'})
            betas = headers.get('Anthropic-Beta', '').split(',')
            self.assertIn('synthetic-body-beta', betas)
            self.assertIn('synthetic-existing-beta', betas)
            self.assertEqual(betas.count(hotfix.BETA), 1)
            self.assertEqual(body['thinking']['block_binding']['prefix_mismatch_behavior'], 'drop_block')
        body, headers = self.send(self.payload(), extra_headers={'X-Test-Policy': 'yes'})
        self.assertEqual(body['thinking']['block_binding']['prefix_mismatch_behavior'], 'error')
        self.assertIn(hotfix.BETA, headers.get('Anthropic-Beta', '').split(','))
        body, _ = self.send({'model': ALIAS, 'reasoning': {'effort': 'high'}, 'input': [{'role': 'user', 'content': 'synthetic empty history'}]})
        self.assertNotIn('block_binding', body.get('thinking', {}))

    def wait_policy(self, expected):
        deadline = time.monotonic() + 4
        while time.monotonic() < deadline:
            body, _ = self.send(self.payload())
            value = body.get('thinking', {}).get('block_binding', {}).get('prefix_mismatch_behavior')
            if value == expected:
                return
            time.sleep(.05)
        self.fail('isolated atomic replacement was not hot reloaded')

    def test_malformed_and_empty_reload_keep_previous_valid_config(self):
        original = self.config_path.read_bytes()
        try:
            for invalid in [b': [unterminated', b'']:
                current = self.config_path.read_bytes()
                hotfix.preserve_inode_write(self.config_path, hotfix.digest(current), invalid, 'test-only')
                time.sleep(.3)  # Intentionally exceed the 150ms reload debounce.
                body, _ = self.send(self.payload())
                self.assertEqual(body['thinking']['block_binding']['prefix_mismatch_behavior'], 'drop_block')
        finally:
            current = self.config_path.read_bytes()
            hotfix.preserve_inode_write(self.config_path, hotfix.digest(current), original, 'test-only')
            self.wait_policy('drop_block')

    def test_preserve_inode_apply_and_rollback_hot_reload_without_restart(self):
        original = self.config_path.read_bytes()
        baseline = copy.deepcopy(self.config)
        baseline['payload']['default'] = baseline['payload']['default'][:3]
        raw = yaml.safe_dump(baseline).encode()
        pid = self.process.pid
        inode = self.config_path.stat().st_ino
        stream_errors = []
        held = self.payload(stream=True)
        held['input'][0]['content'] = 'hold-stream synthetic request'
        self.hold_started.clear()
        self.hold_release.clear()
        def hold_stream():
            try:
                self.send(held)
            except Exception as error:
                stream_errors.append(type(error).__name__)
        stream_thread = threading.Thread(target=hold_stream)
        try:
            hotfix.preserve_inode_write(self.config_path, hotfix.digest(original), raw, 'test-only')
            self.wait_policy(None)
            stream_thread.start()
            self.assertTrue(self.hold_started.wait(2))
            for _ in range(3):
                # Production target URL is changed only in this isolated fixture.
                with mock.patch.object(hotfix, 'targets', return_value=[ALIAS]):
                    result = hotfix.apply(self.config_path, hotfix.digest(raw), self.root / 'hotreload-backups', admin_lock=self.root / 'admin.lock', preserve_config_inode=True)
                self.wait_policy('drop_block')
                hotfix.rollback(result['receipt'], admin_lock=self.root / 'admin.lock', preserve_config_inode=True)
                self.wait_policy(None)
                self.assertTrue(stream_thread.is_alive())
            self.hold_release.set()
            stream_thread.join(3)
            self.assertFalse(stream_thread.is_alive())
            self.assertEqual(stream_errors, [])
            self.assertEqual(self.config_path.read_bytes(), raw)
            self.assertEqual(self.config_path.stat().st_ino, inode)
            self.assertEqual(self.process.pid, pid)
            self.assertIsNone(self.process.poll())
        finally:
            self.hold_release.set()
            if stream_thread.ident is not None:
                stream_thread.join(3)
            current = self.config_path.read_bytes()
            hotfix.preserve_inode_write(self.config_path, hotfix.digest(current), original, 'test-only')
            self.wait_policy('drop_block')

    def test_configured_opus55_exact_alias_is_qualified(self):
        for signature in ['synthetic-opaque-signature', 'claude-redacted-thinking:synthetic-redacted']:
            payload = self.payload(stream=True, signature=signature, tool=True)
            payload['model'] = 'cmd-claude-opus-5-5'
            body, headers = self.send(payload)
            self.assertEqual(body['model'], 'claude-opus-5-5')
            self.assertEqual(body['thinking']['block_binding']['prefix_mismatch_behavior'], 'drop_block')
            self.assertIn(hotfix.BETA, headers.get('Anthropic-Beta', '').split(','))

    def test_forced_tool_choice_is_excluded(self):
        for choice in ['required', {'type': 'function', 'name': 'test_tool'}]:
            payload = self.payload()
            payload['tools'] = [{'type': 'function', 'name': 'test_tool', 'parameters': {'type': 'object'}}]
            payload['tool_choice'] = choice
            body, _ = self.send(payload)
            self.assertNotIn('block_binding', body.get('thinking', {}))

    def test_disabled_empty_and_direct_messages_are_excluded(self):
        for effort, signature in [('none', 'synthetic-opaque-signature'), ('high', ''), (None, 'synthetic-opaque-signature')]:
            with self.subTest(effort=effort, empty_signature=not signature):
                body, _ = self.send(self.payload(signature=signature, effort=effort))
                if effort is None and body.get('thinking', {}).get('type') == 'adaptive':
                    # Provider's own omitted-effort adaptation is within adaptive scope.
                    self.assertEqual(body['thinking']['block_binding']['prefix_mismatch_behavior'], 'drop_block')
                else:
                    self.assertNotIn('block_binding', body.get('thinking', {}))
        body, _ = self.send({'model': ALIAS, 'thinking': {'type': 'adaptive', 'block_binding': {'prefix_mismatch_behavior': 'error'}}, 'max_tokens': 10, 'messages': [{'role': 'assistant', 'content': [{'type': 'thinking', 'thinking': 'synthetic', 'signature': 'synthetic-opaque-signature'}, {'type': 'text', 'text': 'synthetic'}]}, {'role': 'user', 'content': 'synthetic'}]}, '/v1/messages')
        self.assertEqual(body['thinking']['block_binding']['prefix_mismatch_behavior'], 'error')


if __name__ == '__main__':
    unittest.main(verbosity=2)
